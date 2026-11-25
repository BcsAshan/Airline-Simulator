using Pss.BuildingBlocks.Api;
using Pss.Contracts.Internal;

namespace Pss.Shopping.Api.Infrastructure;

public interface IInventoryClient
{
    Task<List<InvSegment>> SearchSegmentsAsync(SegmentSearchQuery query, CancellationToken ct);
}

public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public async Task<List<InvSegment>> SearchSegmentsAsync(SegmentSearchQuery query, CancellationToken ct)
        => await (await http.PostEnvelopeAsync("internal/inventory/segments/search", query, ct)).ReadDataAsync<List<InvSegment>>(ct);
}

public interface IPricingClient
{
    Task<PriceJourneysResponse> PriceJourneysAsync(PriceJourneysRequest request, CancellationToken ct);
}

public sealed class PricingClient(HttpClient http) : IPricingClient
{
    public async Task<PriceJourneysResponse> PriceJourneysAsync(PriceJourneysRequest request, CancellationToken ct)
        => await (await http.PostEnvelopeAsync("internal/pricing/journeys", request, ct)).ReadDataAsync<PriceJourneysResponse>(ct);
}
