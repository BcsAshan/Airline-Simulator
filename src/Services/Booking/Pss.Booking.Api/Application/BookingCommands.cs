using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Keys;
using Pss.Booking.Api.Domain;
using Pss.Booking.Api.Infrastructure;
using Pss.Contracts.Booking;

namespace Pss.Booking.Api.Application;

/// <summary>Passenger, contact and journey commands on the booking in state (blueprint §13.2–13.4).</summary>
public sealed class BookingCommands(IRefDataClient refData, SellService sell, IConfiguration configuration, TimeProvider clock)
{
    // ------------------------------------------------------------------ passengers

    public async Task UpdatePassengerAsync(DraftContext ctx, string passengerKey, PassengerUpdateRequest request, bool replace, CancellationToken ct)
    {
        var draft = ctx.Require();
        PassengerKey.Parse(passengerKey);
        if (!draft.Booking.Passengers.TryGetValue(passengerKey, out var pax))
            throw DomainException.InvalidKey(passengerKey);

        if (!string.IsNullOrWhiteSpace(request.PassengerTypeCode) &&
            !request.PassengerTypeCode.Equals(pax.PassengerTypeCode, StringComparison.OrdinalIgnoreCase))
            throw new DomainException(ErrorCodes.NotSupportedYet,
                "Changing the passenger type is done with PATCH booking/passengers/{key}/passengerTypeCode (Phase 2).");

        NameRules.Validate(request.Name);

        if (replace)
        {
            pax.Name = CleanName(request.Name) ?? new Name();
            pax.Info = new PassengerInfo();
            pax.CustomerNumber = request.CustomerNumber;
            pax.DiscountCode = request.DiscountCode;
            pax.Program = request.Program;
        }
        else
        {
            if (request.Name is not null)
            {
                var n = CleanName(request.Name)!;
                pax.Name.First = n.First ?? pax.Name.First;
                pax.Name.Middle = n.Middle ?? pax.Name.Middle;
                pax.Name.Last = n.Last ?? pax.Name.Last;
                pax.Name.Title = n.Title ?? pax.Name.Title;
                pax.Name.Suffix = n.Suffix ?? pax.Name.Suffix;
            }
            pax.CustomerNumber = request.CustomerNumber ?? pax.CustomerNumber;
            pax.DiscountCode = request.DiscountCode ?? pax.DiscountCode;
            pax.Program = request.Program ?? pax.Program;
        }

        if (request.Info is not null)
        {
            var info = request.Info;
            if (replace || info.Nationality is not null) pax.Info.Nationality = Upper(info.Nationality);
            if (replace || info.ResidentCountry is not null) pax.Info.ResidentCountry = Upper(info.ResidentCountry);
            if (replace || info.FamilyNumber is not null) pax.Info.FamilyNumber = info.FamilyNumber;
            if (replace || info.Gender is not null) pax.Info.Gender = info.Gender;
            if (replace || info.DeclaredGender is not null)
            {
                pax.Info.DeclaredGender = Upper(info.DeclaredGender);
                // Declared gender wins (blueprint §13.2): F → Female, anything else → Male.
                if (pax.Info.DeclaredGender is { Length: > 0 } dg) pax.Info.Gender = dg == "F" ? Gender.Female : Gender.Male;
            }
            if (replace || info.DateOfBirth is not null)
            {
                pax.Info.DateOfBirth = info.DateOfBirth is null ? null : DateTime.SpecifyKind(info.DateOfBirth.Value.Date, DateTimeKind.Unspecified);
                if (pax.Info.DateOfBirth is not null) await ValidateAgeAsync(draft, pax, ct);
            }
        }

        if (pax.PassengerTypeCode != "CHD")
            pax.WeightCategory = pax.Info.Gender switch
            {
                Gender.Male => WeightCategory.Male,
                Gender.Female => WeightCategory.Female,
                _ => WeightCategory.Unassigned
            };

        if (draft.Committed) AddHistory(draft, ctx, "PassengerUpdated", $"Passenger {passengerKey} updated");
        BookingCalculator.Recalculate(draft.Booking);
        ctx.MarkChanged();
    }

    private async Task ValidateAgeAsync(BookingDraft draft, Passenger pax, CancellationToken ct)
    {
        var ptcs = await refData.GetPassengerTypesAsync(ct);
        if (!ptcs.TryGetValue(pax.PassengerTypeCode, out var ptc)) return;
        var travelDate = draft.Booking.Journeys.Count > 0 ? draft.Booking.Journeys.Min(j => j.Designator.Departure) : clock.GetUtcNow().UtcDateTime;
        var dob = pax.Info.DateOfBirth!.Value;
        if (dob > travelDate)
            throw new DomainException(ErrorCodes.InvalidDateOfBirth, "Date of birth cannot be after the travel date.");
        var age = AgeRules.AgeOn(dob, travelDate);
        if (age < ptc.MinimumAge || age > ptc.MaximumAge)
            throw new DomainException(ErrorCodes.InvalidDateOfBirth,
                $"Passenger age {age} is outside the {ptc.PassengerTypeCode} range {ptc.MinimumAge}-{ptc.MaximumAge}.");
    }

    // ------------------------------------------------------------------ contacts

    public string AddContact(DraftContext ctx, Contact contact)
    {
        var draft = ctx.Require();
        contact.ContactTypeCode = ContactRules.NormalizeType(contact.ContactTypeCode);
        ContactRules.Validate(contact);
        if (draft.Booking.Contacts.ContainsKey(contact.ContactTypeCode))
            throw new DomainException(ErrorCodes.ContactTypeExists, $"A contact of type '{contact.ContactTypeCode}' already exists; use PUT.");
        draft.Booking.Contacts[contact.ContactTypeCode] = Normalize(contact);
        if (draft.Committed) AddHistory(draft, ctx, "ContactAdded", contact.ContactTypeCode);
        ctx.MarkChanged();
        return contact.ContactTypeCode;
    }

    public void ReplaceContact(DraftContext ctx, string contactTypeCode, Contact contact)
    {
        var draft = ctx.Require();
        var type = ContactRules.NormalizeType(contactTypeCode);
        contact.ContactTypeCode = type;
        ContactRules.Validate(contact);
        draft.Booking.Contacts[type] = Normalize(contact);
        if (draft.Committed) AddHistory(draft, ctx, "ContactUpdated", type);
        ctx.MarkChanged();
    }

    public void DeleteContact(DraftContext ctx, string contactTypeCode)
    {
        var draft = ctx.Require();
        var type = ContactRules.NormalizeType(contactTypeCode);
        if (!draft.Booking.Contacts.Remove(type))
            throw new DomainException(ErrorCodes.ContactNotFound, $"No contact of type '{type}' exists.");
        ctx.MarkChanged();
    }

    private static Contact Normalize(Contact c)
    {
        c.Name = CleanName(c.Name);
        c.EmailAddress = c.EmailAddress?.Trim();
        if (string.IsNullOrEmpty(c.EmailAddress) && c.DistributionOption == DistributionOption.Email)
            c.DistributionOption = DistributionOption.None;
        return c;
    }

    // ------------------------------------------------------------------ journeys / reset

    /// <summary>Removes a not-yet-committed journey and releases its held seats. Cancelling committed journeys is Phase 2.</summary>
    public async Task RemoveJourneyAsync(DraftContext ctx, string journeyKey, CancellationToken ct)
    {
        var draft = ctx.Require();
        var journey = draft.Booking.Journeys.FirstOrDefault(j => j.JourneyKey == journeyKey) ?? throw DomainException.InvalidKey(journeyKey);
        var holds = draft.Holds.Where(h => h.JourneyKey == journeyKey).ToList();
        if (holds.Any(h => h.Confirmed))
            throw new DomainException(ErrorCodes.JourneyNotChangeable, "Cancelling a committed journey is delivered in Phase 2.");

        await sell.TryReleaseAsync(holds.Select(h => h.HoldId).ToList(), ct);
        if (SeatDraftOps.HasSeats(draft.Booking)) await sell.TryReleaseSeatsAsync(SeatDraftOps.LegIds(journey), ct);
        SeatDraftOps.RemoveJourneySeats(draft.Booking, journey);
        draft.Booking.Journeys.Remove(journey);
        draft.Holds.RemoveAll(h => h.JourneyKey == journeyKey);
        BookingCalculator.Recalculate(draft.Booking);
        ctx.MarkChanged();
    }

    /// <summary>DELETE booking/reset: drop the draft and release uncommitted holds.</summary>
    public async Task ResetAsync(DraftContext ctx, CancellationToken ct)
    {
        if (ctx.Draft is null) return;
        await sell.TryReleaseAsync(ctx.Draft.UnconfirmedHoldIds(), ct);
        if (ctx.Draft.SeatsChanged) await sell.TryReleaseSeatsAsync(null, ct);
        ctx.Clear();
    }

    public (bool Allowed, DateTime? Expiration) HoldAvailability(BookingDraft draft)
        => HoldRules.Evaluate(draft.Booking, clock.GetUtcNow().UtcDateTime,
            configuration.GetValue("Booking:HoldMinHoursBeforeDeparture", 72),
            configuration.GetValue("Booking:HoldMaxHours", 24));

    public void AddHistory(BookingDraft draft, DraftContext ctx, string evt, string? details)
        => draft.Booking.History.Add(new BookingHistoryItem
        {
            CreatedDate = clock.GetUtcNow().UtcDateTime,
            Event = evt,
            HistoryCategory = "Booking",
            Details = details,
            PointOfSale = ctx.Session.ToPointOfSale()
        });

    private static Name? CleanName(Name? n) => n is null
        ? null
        : new Name
        {
            First = NameRules.Clean(n.First),
            Middle = NameRules.Clean(n.Middle),
            Last = NameRules.Clean(n.Last),
            Title = NameRules.Clean(n.Title)?.ToUpperInvariant(),
            Suffix = NameRules.Clean(n.Suffix)
        };

    private static string? Upper(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();
}
