using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Keys;
using Pss.Booking.Api.Domain;
using Pss.Booking.Api.Infrastructure;
using Pss.Contracts.Booking;
using Pss.Contracts.Events;
using Pss.Contracts.Internal;
using BookingModel = Pss.Contracts.Booking.Booking;

namespace Pss.Booking.Api.Application;

/// <summary>
/// Commit (POST|PUT /api/nsk/v3/booking) — blueprint §12.3.
/// Phase 1 runs the steps synchronously: validate → reserve record locator → authorise payments → confirm holds →
/// persist + BookingCommitted (transactional outbox). Compensation: void payments if inventory confirm fails.
/// TODO(Phase 2/4): MassTransit state-machine saga for async PSP flows (3DS, pending payments, 202 + status polling).
/// </summary>
public sealed class CommitService(
    BookingDbContext db,
    IPublishEndpoint publisher,
    IInventoryClient inventory,
    IPaymentClient payments,
    ISeatClient seats,
    DraftStore drafts,
    BookingCommands commands,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<CommitService> logger)
{
    private string OwningCarrier => configuration["Booking:CarrierCode"] ?? "XX";

    public async Task CommitAsync(DraftContext ctx, CommitRequest request, bool isUpdate, CancellationToken ct)
    {
        var draft = ctx.Require();
        var booking = draft.Booking;
        var session = ctx.Session;
        var now = clock.GetUtcNow().UtcDateTime;

        // ---- 1. validate (blueprint §9 invariants) -------------------------------------------------
        Validate(booking);
        BookingCalculator.Recalculate(booking);

        var pending = booking.Payments
            .Where(p => PaymentKey.Parse(p.PaymentKey).PaymentId == 0 && p.Status != BookingPaymentStatus.Declined)
            .ToList();
        var projectedBalance = booking.Breakdown.BalanceDue; // assumes all non-declined payments succeed
        var (holdAllowed, holdExpiration) = commands.HoldAvailability(draft);
        if (projectedBalance > 0 && !holdAllowed)
            throw new DomainException(ErrorCodes.BalanceDueNotAllowed,
                $"The booking has a balance due of {projectedBalance} {booking.CurrencyCode} and cannot be held; add a payment.");
        if (projectedBalance < 0)
            throw new DomainException(ErrorCodes.InvalidPaymentAmount, "Payments exceed the booking total (refunds arrive in Phase 2).");

        // ---- 2. reserve booking id + record locator ------------------------------------------------
        var isNew = !draft.Committed;
        var record = isNew ? await CreatePlaceholderAsync(session.AgentCode, session.ApplicationName, now, ct) : await LoadRecordAsync(draft, ct);

        var approvedThisCommit = new List<long>();
        List<long>? seatIdsConfirmed = null;
        var seatRefs = SeatDraftOps.SeatRefs(booking);
        try
        {
            // ---- 3. authorise payments ---------------------------------------------------------------
            var results = new List<PaymentAuthResult>();
            if (pending.Count > 0)
            {
                results = (await payments.AuthorizeAsync(new AuthorizePaymentsRequest
                {
                    BookingId = record.BookingId,
                    RecordLocator = record.RecordLocator,
                    Payments = pending
                }, ct)).Results;
                approvedThisCommit.AddRange(results.Where(r => r.Status == BookingPaymentStatus.Approved).Select(r => r.PaymentId));
            }

            var declined = results.Where(r => r.Status == BookingPaymentStatus.Declined).ToList();
            if (declined.Count > 0)
            {
                var declinedAmount = pending.Where(p => declined.Any(d => d.DraftPaymentKey == p.PaymentKey)).Sum(p => p.Amounts.Amount);
                if (projectedBalance + declinedAmount > 0 && !holdAllowed)
                {
                    foreach (var d in declined)
                    {
                        var p = booking.Payments.First(x => x.PaymentKey == d.DraftPaymentKey);
                        p.Status = BookingPaymentStatus.Declined;
                        p.AuthorizationStatus = d.AuthorizationStatus;
                    }
                    BookingCalculator.Recalculate(booking);
                    await drafts.SaveAsync(session, draft); // keep the declined status visible to the client
                    throw new DomainException(ErrorCodes.PaymentDeclined,
                        "The payment was declined. Add another payment and commit again.", 400,
                        new { declined = declined.Select(d => new { paymentKey = d.DraftPaymentKey, reason = d.DeclineReason }) });
                }
            }

            // ---- 4a. confirm seats (Seat service) ------------------------------------------------------
            // Before inventory: losing a held seat (session expired, swept) is the likelier failure, and a failed
            // first commit can hand confirmed seats back to the session (revert) but not unconfirm inventory.
            if (draft.SeatsChanged)
            {
                seatIdsConfirmed = (await seats.ConfirmAsync(new ConfirmSeatsRequest
                {
                    BookingId = record.BookingId,
                    RecordLocator = record.RecordLocator,
                    Seats = seatRefs
                }, ct)).ConfirmedAssignmentIds;
            }

            // ---- 4b. confirm inventory holds ---------------------------------------------------------
            var unconfirmed = draft.UnconfirmedHoldIds();
            if (unconfirmed.Count > 0)
                await inventory.ConfirmAsync(new ConfirmHoldsRequest { HoldIds = unconfirmed, RecordLocator = record.RecordLocator }, ct);

            // ---- 5. apply results to the aggregate ---------------------------------------------------
            foreach (var r in results)
            {
                var p = booking.Payments.First(x => x.PaymentKey == r.DraftPaymentKey);
                var oldKey = PaymentKey.Parse(p.PaymentKey);
                p.Status = r.Status;
                p.AuthorizationStatus = r.AuthorizationStatus;
                p.AuthorizationCode = r.AuthorizationCode;
                p.TransactionCode = r.TransactionCode;
                p.ApprovalDate = r.ApprovalDate;
                p.ModifiedDate = now;
                if (r.PaymentId > 0) p.PaymentKey = new PaymentKey(r.PaymentId, oldKey.Sequence, p.Code, record.BookingId).Encode();
            }
            foreach (var h in draft.Holds) h.Confirmed = true;
            foreach (var s in booking.Journeys.SelectMany(j => j.Segments)) s.Status = SegmentStatus.HeldConfirmed;

            BookingCalculator.Recalculate(booking);
            ApplyStatus(booking, holdAllowed, holdExpiration);

            booking.BookingKey = new BookingKey(record.BookingId, record.RecordLocator, false).Encode();
            booking.RecordLocator = record.RecordLocator;
            booking.Locators.NumericRecordLocator = record.BookingId.ToString().PadLeft(12, '0');
            booking.Info.OwningCarrierCode = OwningCarrier;
            booking.Info.CreatedDate ??= now;
            booking.Info.ModifiedDate = now;
            booking.Info.ExpirationDate = HoldRules.FirstDepartureUtc(booking);
            if (isNew) booking.Info.CreatedAgentId = session.AgentId;
            booking.Info.ModifiedAgentId = session.AgentId;
            booking.Sales.Modified = session.ToPointOfSale();

            if (!string.IsNullOrWhiteSpace(request.ReceivedBy))
            {
                booking.ReceivedBy ??= new ReceivedBy { ReceivedByName = request.ReceivedBy };
                booking.ReceivedBy.LatestReceivedBy = request.ReceivedBy;
            }
            foreach (var c in request.Comments ?? new List<CommitComment>())
            {
                if (string.IsNullOrWhiteSpace(c.Text)) continue;
                booking.Comments.Add(new BookingComment
                {
                    CommentKey = KeyCodec.Encode('!', record.BookingId, ++draft.CommentSequence),
                    Type = c.Type,
                    Text = c.Text.Length > 800 ? c.Text[..800] : c.Text,
                    CreatedDate = now,
                    PointOfSale = session.ToPointOfSale()
                });
            }
            if (request.DistributionOption is not null && booking.Contacts.TryGetValue("P", out var primary))
                primary.DistributionOption = request.DistributionOption.Value;

            commands.AddHistory(draft, ctx, isNew ? "BookingCreated" : "BookingModified",
                $"Status {booking.Info.Status}, balance {booking.Breakdown.BalanceDue} {booking.CurrencyCode}");

            // ---- 6. persist + publish (outbox) ---------------------------------------------------------
            await PersistAsync(record, draft, isNew, request, session.AgentCode, now, ct);
        }
        catch (Exception ex)
        {
            // Compensation runs with CancellationToken.None so a cancelled/timed-out request still cleans up.
            if (approvedThisCommit.Count > 0) await TryVoidAsync(approvedThisCommit, ex.Message, CancellationToken.None);
            if (seatIdsConfirmed is { Count: > 0 })
                await TryRevertSeatsAsync(new RevertSeatsRequest { BookingId = record.BookingId, AssignmentIds = seatIdsConfirmed, NewBooking = isNew });
            else if (seatIdsConfirmed is null && isNew && draft.SeatsChanged)
                // confirm may have succeeded in Seat even though we saw an error (timeout): the placeholder's seats are all new
                await TryRevertSeatsAsync(new RevertSeatsRequest { BookingId = record.BookingId, NewBooking = true });
            if (isNew) await TryDeletePlaceholderAsync(record.BookingId, CancellationToken.None);
            throw;
        }

        // Seats the booking no longer uses (changed/removed on a committed booking) are freed only now that the new
        // version is saved. Best effort: if it fails the old seat stays blocked, which never double-sells a seat.
        if (draft.SeatsChanged && draft.Committed) await TryFinalizeSeatsAsync(record.BookingId, seatRefs);

        draft.Committed = true;
        draft.SeatsChanged = false;
        draft.BookingId = record.BookingId;
        draft.RowVersion = Convert.ToBase64String(record.RowVer);
        ctx.MarkChanged();
    }

    private static void Validate(BookingModel booking)
    {
        if (booking.Journeys.Count == 0) throw new DomainException(ErrorCodes.NoJourneys, "The booking has no journeys.");
        if (!booking.Contacts.TryGetValue("P", out var primary))
            throw new DomainException(ErrorCodes.MissingPrimaryContact, "Exactly one primary (P) contact is required.");
        if (string.IsNullOrWhiteSpace(primary.Name?.Last) && string.IsNullOrWhiteSpace(primary.EmailAddress))
            throw new DomainException(ErrorCodes.MissingPrimaryContact, "The primary contact needs a name or an email address.");

        foreach (var pax in booking.Passengers.Values)
        {
            if (string.IsNullOrWhiteSpace(pax.Name.First))
                throw new DomainException(ErrorCodes.PassengerFieldRequired("FirstName"), $"First name is required for passenger {pax.PassengerKey}.");
            if (string.IsNullOrWhiteSpace(pax.Name.Last))
                throw new DomainException(ErrorCodes.PassengerFieldRequired("LastName"), $"Last name is required for passenger {pax.PassengerKey}.");
        }

        if (booking.Info.PriceStatus == PriceStatus.Invalid)
            throw new DomainException(ErrorCodes.FailedFareRestriction, "The booking must be re-priced before commit.");
    }

    private static void ApplyStatus(BookingModel booking, bool holdAllowed, DateTime? holdExpiration)
    {
        if (booking.Breakdown.BalanceDue <= 0)
        {
            booking.Info.Status = BookingStatus.Confirmed;
            booking.Hold = null;
        }
        else if (holdAllowed)
        {
            booking.Info.Status = BookingStatus.Hold;
            booking.Hold = new BookingHold { Expiration = holdExpiration };
        }
        else
        {
            throw new DomainException(ErrorCodes.BalanceDueNotAllowed, "The booking cannot be held with a balance due.");
        }
    }

    private async Task<BookingRecord> CreatePlaceholderAsync(string agentCode, string applicationName, DateTime now, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var record = new BookingRecord
            {
                RecordLocator = RecordLocatorGenerator.Next(),
                Status = (int)BookingStatus.Default,
                CreatedUtc = now,
                ModifiedUtc = now,
                CreatedAgentCode = agentCode,
                ApplicationName = applicationName
            };
            db.Bookings.Add(record);
            try
            {
                await db.SaveChangesAsync(ct);
                return record;
            }
            catch (DbUpdateException) when (attempt < 5)
            {
                // Record locator collision (unique index) — try another one.
                db.Entry(record).State = EntityState.Detached;
            }
        }
    }

    private async Task<BookingRecord> LoadRecordAsync(BookingDraft draft, CancellationToken ct)
    {
        var record = await db.Bookings.FirstOrDefaultAsync(b => b.BookingId == draft.BookingId, ct)
                     ?? throw new DomainException(ErrorCodes.NoBookingInState, "The committed booking no longer exists.");
        if (!string.IsNullOrEmpty(draft.RowVersion))
            db.Entry(record).Property(r => r.RowVer).OriginalValue = Convert.FromBase64String(draft.RowVersion);
        return record;
    }

    private async Task PersistAsync(BookingRecord record, BookingDraft draft, bool isNew, CommitRequest request,
        string agentCode, DateTime now, CancellationToken ct)
    {
        var booking = draft.Booking;
        booking.Contacts.TryGetValue("P", out var primary);

        record.Status = (int)booking.Info.Status;
        record.PaidStatus = (int)booking.Info.PaidStatus;
        record.CurrencyCode = booking.CurrencyCode;
        record.TotalAmount = booking.Breakdown.TotalAmount;
        record.BalanceDue = booking.Breakdown.BalanceDue;
        record.ModifiedUtc = now;
        record.HoldExpirationUtc = booking.Hold?.Expiration;
        record.FirstDepartureUtc = HoldRules.FirstDepartureUtc(booking);
        record.ContactEmail = primary?.EmailAddress;
        record.ContactLastName = primary?.Name?.Last;
        record.Document = JsonSerializer.Serialize(booking, PssJson.Options);

        // Search projections (rebuilt on every commit) — removed/added through the change tracker so the rebuild,
        // the booking update and the outbox message all commit in the same SaveChanges transaction.
        db.PassengerIndex.RemoveRange(await db.PassengerIndex.Where(x => x.BookingId == record.BookingId).ToListAsync(ct));
        db.SegmentIndex.RemoveRange(await db.SegmentIndex.Where(x => x.BookingId == record.BookingId).ToListAsync(ct));
        db.PassengerIndex.AddRange(booking.Passengers.Values.Select(p => new BookingPassengerIndex
        {
            BookingId = record.BookingId, PassengerKey = p.PassengerKey, PassengerTypeCode = p.PassengerTypeCode,
            FirstName = p.Name.First, LastName = p.Name.Last
        }));
        db.SegmentIndex.AddRange(booking.Journeys.SelectMany(j => j.Segments).Select(s => new BookingSegmentIndex
        {
            BookingId = record.BookingId, CarrierCode = s.Identifier.CarrierCode, FlightNumber = s.Identifier.Identifier,
            Origin = s.Designator.Origin, Destination = s.Designator.Destination, DepartureLocal = s.Designator.Departure,
            ClassOfService = s.Fares.FirstOrDefault()?.ClassOfService ?? string.Empty
        }));
        db.History.Add(new BookingHistoryRecord
        {
            BookingId = record.BookingId, CreatedUtc = now, Event = isNew ? "BookingCreated" : "BookingModified",
            Details = $"Status={booking.Info.Status}; Balance={booking.Breakdown.BalanceDue}", AgentCode = agentCode
        });

        await publisher.Publish(new BookingCommitted(
            record.BookingId,
            record.RecordLocator,
            (int)booking.Info.Status,
            booking.CurrencyCode,
            booking.Breakdown.TotalAmount,
            booking.Breakdown.BalanceDue,
            isNew,
            primary?.EmailAddress,
            primary?.Name?.First,
            primary?.Name?.Last,
            request.NotifyContacts == false ? 0 : (int)(primary?.DistributionOption ?? DistributionOption.None),
            booking.Journeys.SelectMany(j => j.Segments).Select(s => new BookedSegment(
                s.Identifier.CarrierCode, s.Identifier.Identifier, s.Designator.Origin, s.Designator.Destination,
                s.Designator.Departure, s.Designator.Arrival, s.Fares.FirstOrDefault()?.ClassOfService ?? string.Empty)).ToList(),
            booking.Passengers.Values.Select(p => new BookedPassenger(p.PassengerKey, p.PassengerTypeCode, p.Name.First, p.Name.Last)).ToList(),
            now), ct);

        try
        {
            await db.SaveChangesAsync(ct); // booking + projections + history + outbox message in one transaction
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainException.Conflict(ErrorCodes.ModifiedByAnotherSession,
                "The booking was modified by another session. Retrieve it again and re-apply your changes.");
        }
    }

    private async Task TryVoidAsync(List<long> paymentIds, string reason, CancellationToken ct)
    {
        try
        {
            await payments.VoidAsync(new VoidPaymentsRequest { PaymentIds = paymentIds, Reason = reason }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not void payments {PaymentIds} after a failed commit — manual follow-up required", paymentIds);
        }
    }

    private async Task TryRevertSeatsAsync(RevertSeatsRequest request)
    {
        try
        {
            await seats.RevertAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not hand seats of failed booking {BookingId} back to the session", request.BookingId);
        }
    }

    private async Task TryFinalizeSeatsAsync(long bookingId, List<SeatAssignmentRef> seatRefs)
    {
        try
        {
            await seats.FinalizeAsync(new FinalizeSeatsRequest { BookingId = bookingId, Seats = seatRefs }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not release replaced seats of booking {BookingId}; they stay blocked", bookingId);
        }
    }

    private async Task TryDeletePlaceholderAsync(long bookingId, CancellationToken ct)
    {
        try
        {
            await db.Bookings.Where(b => b.BookingId == bookingId && b.Status == (int)BookingStatus.Default).ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete placeholder booking {BookingId}", bookingId);
        }
    }
}
