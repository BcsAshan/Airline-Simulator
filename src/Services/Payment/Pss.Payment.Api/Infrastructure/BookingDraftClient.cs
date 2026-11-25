using Pss.BuildingBlocks.Api;
using Pss.Contracts.Internal;

namespace Pss.Payment.Api.Infrastructure;

/// <summary>Booking is the single writer of the booking in state; Payment changes it only through this internal API.</summary>
public interface IBookingDraftClient
{
    Task<DraftSummary> GetSummaryAsync(CancellationToken ct);
    Task<string> AddPaymentAsync(AddDraftPaymentRequest request, CancellationToken ct);
    Task DeletePaymentAsync(string paymentKey, CancellationToken ct);
}

public sealed class BookingDraftClient(HttpClient http) : IBookingDraftClient
{
    public async Task<DraftSummary> GetSummaryAsync(CancellationToken ct)
        => await (await http.GetAsync("internal/booking/draft", ct)).ReadDataAsync<DraftSummary>(ct);

    public async Task<string> AddPaymentAsync(AddDraftPaymentRequest request, CancellationToken ct)
        => (await (await http.PostEnvelopeAsync("internal/booking/draft/payments", request, ct)).ReadDataAsync<AddDraftPaymentResponse>(ct)).PaymentKey;

    public async Task DeletePaymentAsync(string paymentKey, CancellationToken ct)
        => await (await http.DeleteAsync("internal/booking/draft/payments/" + Uri.EscapeDataString(paymentKey), ct)).EnsureEnvelopeSuccessAsync(ct);
}
