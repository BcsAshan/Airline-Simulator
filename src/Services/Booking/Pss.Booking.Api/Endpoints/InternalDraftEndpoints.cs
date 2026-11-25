using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Keys;
using Pss.BuildingBlocks.Session;
using Pss.Booking.Api.Application;
using Pss.Booking.Api.Domain;
using Pss.Booking.Api.Infrastructure;
using Pss.Contracts.Booking;
using Pss.Contracts.Internal;

namespace Pss.Booking.Api.Endpoints;

/// <summary>
/// Internal draft command API (blueprint §8.6): other services (Payment now; Seat/Ancillary/DCS later) change the
/// booking in state only through Booking, which stays the single writer of the draft. Uses the caller's forwarded token.
/// </summary>
public static class InternalDraftEndpoints
{
    public static WebApplication MapInternalDraftEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/internal/booking/draft").WithTags("Internal").RequirePermission(PermissionLevel.Anonymous);

        g.MapGet("", async (HttpContext http, DraftStore drafts) =>
        {
            var draft = await BookingEndpoints.RequireDraftAsync(http, drafts);
            BookingCalculator.Recalculate(draft.Booking);
            return Envelope.Ok(new DraftSummary
            {
                BookingId = draft.BookingId,
                RecordLocator = draft.Booking.RecordLocator,
                CurrencyCode = draft.Booking.CurrencyCode,
                BalanceDue = draft.Booking.Breakdown.BalanceDue,
                TotalAmount = draft.Booking.Breakdown.TotalAmount,
                Payments = draft.Booking.Payments
            });
        });

        g.MapPost("/payments", async (AddDraftPaymentRequest request, HttpContext http, StatefulBookingExecutor exec, TimeProvider clock, CancellationToken ct) =>
        {
            var session = http.GetSession();
            var key = await exec.ExecuteAsync(session, c =>
            {
                var draft = c.Require();
                BookingCalculator.Recalculate(draft.Booking);
                var payment = request.Payment;
                if (payment.Amounts.Amount <= 0)
                    throw new DomainException(ErrorCodes.InvalidPaymentAmount, "The payment amount must be greater than zero.");
                if (payment.Amounts.Amount > draft.Booking.Breakdown.BalanceDue)
                    throw new DomainException(ErrorCodes.InvalidPaymentAmount,
                        $"The payment amount exceeds the balance due ({draft.Booking.Breakdown.BalanceDue} {draft.Booking.CurrencyCode}).");

                var seq = ++draft.PaymentSequence;
                payment.PaymentKey = new PaymentKey(0, seq, payment.Code, draft.BookingId ?? 0).Encode();
                payment.CreatedDate = clock.GetUtcNow().UtcDateTime;
                payment.AddedToState = true;
                payment.ChannelType = session.ChannelType;
                payment.PointOfSale = session.ToPointOfSale();
                payment.SourcePointOfSale = session.ToPointOfSale();
                draft.Booking.Payments.Add(payment);
                BookingCalculator.Recalculate(draft.Booking);
                c.MarkChanged();
                return Task.FromResult(payment.PaymentKey);
            }, ct);
            return Envelope.Ok(new AddDraftPaymentResponse { PaymentKey = key });
        });

        g.MapDelete("/payments/{paymentKey}", async (string paymentKey, HttpContext http, StatefulBookingExecutor exec, CancellationToken ct) =>
        {
            await exec.ExecuteAsync(http.GetSession(), c =>
            {
                var draft = c.Require();
                var payment = draft.Booking.Payments.FirstOrDefault(p => p.PaymentKey == paymentKey)
                              ?? throw new DomainException(ErrorCodes.PaymentKeyNotFound, "The payment was not found on the booking.");
                if (PaymentKey.Parse(paymentKey).PaymentId != 0)
                    throw new DomainException(ErrorCodes.PaymentNotRemovable, "Committed payments cannot be deleted (use a refund or reversal).");
                draft.Booking.Payments.Remove(payment);
                BookingCalculator.Recalculate(draft.Booking);
                c.MarkChanged();
                return Task.FromResult(true);
            }, ct);
            return Envelope.OkNoData();
        });

        return app;
    }
}
