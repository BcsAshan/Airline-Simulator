using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Booking;
using Pss.Contracts.Internal;
using Pss.Payment.Api.Application;
using Pss.Payment.Api.Infrastructure;

namespace Pss.Payment.Api.Endpoints;

public static class PaymentEndpoints
{
    public static WebApplication MapPaymentEndpoints(this WebApplication app)
    {
        var nsk = app.MapGroup("/api/nsk").WithTags("Payments").RequirePermission(PermissionLevel.Anonymous);

        nsk.MapGet("/v2/booking/payments/available", async (HttpContext http, PaymentService payments, IBookingDraftClient booking, CancellationToken ct) =>
        {
            await booking.GetSummaryAsync(ct); // requires a booking in state (NoBookingInState otherwise)
            return Envelope.Ok(payments.AvailableMethods(http.GetSession()));
        });

        // v6 is current; v4 kept because the Navitaire HAR/Postman flows use it.
        foreach (var v in new[] { "v4", "v6" })
        {
            nsk.MapPost($"/{v}/booking/payments", async (AddPaymentRequest? request, HttpContext http, PaymentService payments, CancellationToken ct) =>
            {
                if (request is null) throw new DomainException(ErrorCodes.NullRequest, "The request body is required.");
                var key = await payments.AddToBookingAsync(http.GetSession(), request, ct);
                return Envelope.Created($"/api/nsk/v1/booking/payments/{key}");
            });
        }

        nsk.MapGet("/v1/booking/payments", async (IBookingDraftClient booking, CancellationToken ct)
            => Envelope.Ok((await booking.GetSummaryAsync(ct)).Payments));

        nsk.MapGet("/v1/booking/payments/{paymentKey}", async (string paymentKey, IBookingDraftClient booking, CancellationToken ct) =>
        {
            var payment = (await booking.GetSummaryAsync(ct)).Payments.FirstOrDefault(p => p.PaymentKey == paymentKey)
                          ?? throw new DomainException(ErrorCodes.PaymentKeyNotFound, "The payment was not found on the booking.");
            return Envelope.Ok(payment);
        });

        nsk.MapDelete("/v1/booking/payments/{paymentKey}", async (string paymentKey, IBookingDraftClient booking, CancellationToken ct) =>
        {
            await booking.DeletePaymentAsync(paymentKey, ct);
            return Envelope.OkNoData();
        });

        // ---- internal (Booking commit orchestration) ----
        var internalApi = app.MapGroup("/internal/payments").WithTags("Internal").RequirePermission(PermissionLevel.Anonymous);
        internalApi.MapPost("/authorize", async (AuthorizePaymentsRequest request, PaymentService payments, CancellationToken ct)
            => Envelope.Ok(await payments.AuthorizeAsync(request, ct)));
        internalApi.MapPost("/void", async (VoidPaymentsRequest request, PaymentService payments, CancellationToken ct) =>
        {
            await payments.VoidAsync(request, ct);
            return Envelope.OkNoData();
        });

        return app;
    }
}
