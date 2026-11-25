using Pss.BuildingBlocks.Domain;
using Pss.Booking.Api.Domain;
using Pss.Contracts.Booking;
using Pss.Contracts.Internal;
using BookingModel = Pss.Contracts.Booking.Booking;

namespace Pss.Booking.Api.Application;

/// <summary>
/// POST /api/nsk/v4|v5/trip — one-shot booking: sell + passenger details + contacts in one stateful call
/// (Navitaire recommends it when all data is known up front). Starts a new booking in state.
/// </summary>
public sealed class TripService(SellService sell, BookingCommands commands)
{
    public async Task<BookingModel> CreateAsync(DraftContext ctx, TripRequest request, CancellationToken ct)
    {
        if (request.Passengers.Count == 0)
            throw new DomainException(ErrorCodes.NullRequest, "At least one passenger is required.");
        if (request.Passengers.Any(p => p.Infant is not null))
            throw new DomainException(ErrorCodes.NotSupportedYet, "Infants are delivered in Phase 2.");

        if (ctx.Draft is not null)
        {
            if (!ctx.Draft.Committed) await sell.TryReleaseAsync(ctx.Draft.UnconfirmedHoldIds(), ct);
            if (ctx.Draft.SeatsChanged) await sell.TryReleaseSeatsAsync(null, ct);
            ctx.Clear();
        }

        var types = request.Passengers
            .Select(p => (p.Passenger?.PassengerTypeCode ?? "ADT").Trim().ToUpperInvariant())
            .GroupBy(t => t)
            .Select(g => new PaxTypeCount { Type = g.Key, Count = g.Count() })
            .ToList();

        await sell.SellAsync(ctx, new TripSellRequest
        {
            Keys = request.Journeys?.Keys ?? new List<SellKey>(),
            Passengers = new SellPassengers { Types = types },
            CurrencyCode = request.CurrencyCode,
            PromotionCode = request.PromotionCode
        }, ct);

        var draft = ctx.Require();
        var unassigned = draft.Booking.Passengers.Values.OrderBy(p => p.PassengerKey).ToList();
        foreach (var wrapper in request.Passengers)
        {
            var ptc = (wrapper.Passenger?.PassengerTypeCode ?? "ADT").Trim().ToUpperInvariant();
            var pax = unassigned.First(p => p.PassengerTypeCode == ptc);
            unassigned.Remove(pax);
            if (wrapper.Passenger is not null)
                await commands.UpdatePassengerAsync(ctx, pax.PassengerKey, wrapper.Passenger, replace: true, ct);
        }

        var contacts = new List<Contact>(request.Contacts ?? new List<Contact>());
        if (request.Contact is not null) contacts.Add(request.Contact);
        foreach (var contact in contacts)
        {
            var type = ContactRules.NormalizeType(string.IsNullOrWhiteSpace(contact.ContactTypeCode) ? "P" : contact.ContactTypeCode);
            if (draft.Booking.Contacts.ContainsKey(type)) commands.ReplaceContact(ctx, type, contact);
            else
            {
                contact.ContactTypeCode = type;
                commands.AddContact(ctx, contact);
            }
        }

        BookingCalculator.Recalculate(draft.Booking);
        ctx.MarkChanged();
        return draft.Booking;
    }
}
