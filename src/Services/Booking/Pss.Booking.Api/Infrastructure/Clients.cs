using Microsoft.Extensions.Caching.Memory;
using Pss.BuildingBlocks.Api;
using Pss.Contracts.Internal;

namespace Pss.Booking.Api.Infrastructure;

public interface IShoppingClient
{
    Task<OfferJourneyFare> GetOfferAsync(string fareAvailabilityKey, CancellationToken ct);
}

public sealed class ShoppingClient(HttpClient http) : IShoppingClient
{
    public async Task<OfferJourneyFare> GetOfferAsync(string fareAvailabilityKey, CancellationToken ct)
        => await (await http.GetAsync("internal/shopping/offers/" + Uri.EscapeDataString(fareAvailabilityKey), ct)).ReadDataAsync<OfferJourneyFare>(ct);
}

public interface IPricingClient
{
    Task<PriceSelectionResponse> PriceSelectionAsync(PriceSelectionRequest request, CancellationToken ct);
}

public sealed class PricingClient(HttpClient http) : IPricingClient
{
    public async Task<PriceSelectionResponse> PriceSelectionAsync(PriceSelectionRequest request, CancellationToken ct)
        => await (await http.PostEnvelopeAsync("internal/pricing/selection", request, ct)).ReadDataAsync<PriceSelectionResponse>(ct);
}

public interface IInventoryClient
{
    Task<HoldResponse> HoldAsync(HoldRequest request, CancellationToken ct);
    Task ConfirmAsync(ConfirmHoldsRequest request, CancellationToken ct);
    Task ReleaseAsync(IReadOnlyCollection<long> holdIds, CancellationToken ct);
}

public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public async Task<HoldResponse> HoldAsync(HoldRequest request, CancellationToken ct)
        => await (await http.PostEnvelopeAsync("internal/inventory/holds", request, ct)).ReadDataAsync<HoldResponse>(ct);

    public async Task ConfirmAsync(ConfirmHoldsRequest request, CancellationToken ct)
        => await (await http.PostEnvelopeAsync("internal/inventory/holds/confirm", request, ct)).EnsureEnvelopeSuccessAsync(ct);

    public async Task ReleaseAsync(IReadOnlyCollection<long> holdIds, CancellationToken ct)
    {
        if (holdIds.Count == 0) return;
        await (await http.PostEnvelopeAsync("internal/inventory/holds/release", new ReleaseHoldsRequest { HoldIds = holdIds.ToList() }, ct))
            .EnsureEnvelopeSuccessAsync(ct);
    }
}

public interface IPaymentClient
{
    Task<AuthorizePaymentsResponse> AuthorizeAsync(AuthorizePaymentsRequest request, CancellationToken ct);
    Task VoidAsync(VoidPaymentsRequest request, CancellationToken ct);
}

public sealed class PaymentClient(HttpClient http) : IPaymentClient
{
    public async Task<AuthorizePaymentsResponse> AuthorizeAsync(AuthorizePaymentsRequest request, CancellationToken ct)
        => await (await http.PostEnvelopeAsync("internal/payments/authorize", request, ct)).ReadDataAsync<AuthorizePaymentsResponse>(ct);

    public async Task VoidAsync(VoidPaymentsRequest request, CancellationToken ct)
        => await (await http.PostEnvelopeAsync("internal/payments/void", request, ct)).EnsureEnvelopeSuccessAsync(ct);
}

public sealed class PassengerTypeInfo
{
    public string PassengerTypeCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int MinimumAge { get; set; }
    public int MaximumAge { get; set; }
    public bool NonAdult { get; set; }
    public bool Allowed { get; set; } = true;
    public bool InActive { get; set; }
}

public interface IRefDataClient
{
    Task<IReadOnlyDictionary<string, PassengerTypeInfo>> GetPassengerTypesAsync(CancellationToken ct);
}

/// <summary>Reads RefData's public resource endpoint (forwarding the caller's token) and caches it.</summary>
public sealed class RefDataClient(HttpClient http, IMemoryCache cache) : IRefDataClient
{
    public async Task<IReadOnlyDictionary<string, PassengerTypeInfo>> GetPassengerTypesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("booking:ptc", out IReadOnlyDictionary<string, PassengerTypeInfo>? cached) && cached is not null)
            return cached;

        var list = await (await http.GetAsync("api/nsk/v1/resources/passengerTypes", ct)).ReadDataAsync<List<PassengerTypeInfo>>(ct);
        IReadOnlyDictionary<string, PassengerTypeInfo> map = list.ToDictionary(p => p.PassengerTypeCode, StringComparer.OrdinalIgnoreCase);
        cache.Set("booking:ptc", map, TimeSpan.FromMinutes(30));
        return map;
    }
}
