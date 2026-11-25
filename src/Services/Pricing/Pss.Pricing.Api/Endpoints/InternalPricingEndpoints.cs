using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Booking;
using Pss.Contracts.Internal;
using Pss.Pricing.Api.Domain;
using Pss.Pricing.Api.Infrastructure;

namespace Pss.Pricing.Api.Endpoints;

public static class InternalPricingEndpoints
{
    public static WebApplication MapInternalPricingEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/internal/pricing").WithTags("Internal").RequirePermission(PermissionLevel.Anonymous);

        // Shopping: fare options for many journeys at once.
        g.MapPost("/journeys", async (PriceJourneysRequest request, PricingSnapshotProvider provider, IConfiguration cfg, CancellationToken ct) =>
        {
            EnsureCurrency(request.CurrencyCode, cfg);
            var snapshot = await provider.GetAsync(ct);
            var response = new PriceJourneysResponse
            {
                Journeys = request.Journeys.Select(j => new PricedJourney
                {
                    Ref = j.Ref,
                    Options = PricingEngine.PriceJourney(j, request.Passengers, snapshot, request.MaxFaresPerJourney)
                }).ToList()
            };
            return Envelope.Ok(response);
        });

        // Booking: re-price the fares chosen at shopping time for the actual passenger mix (trip sell).
        g.MapPost("/selection", async (PriceSelectionRequest request, PricingSnapshotProvider provider, IConfiguration cfg, CancellationToken ct) =>
        {
            EnsureCurrency(request.CurrencyCode, cfg);
            var snapshot = await provider.GetAsync(ct);
            var paxTypes = request.Passengers.Where(p => p.Count > 0).Select(p => p.Type).Distinct().ToList();
            var response = new PriceSelectionResponse();
            foreach (var seg in request.Segments)
            {
                var fare = snapshot.Fares.FirstOrDefault(f => f.FareId == seg.FareId && !f.InActive);
                if (fare is null || fare.Origin != seg.Origin || fare.Destination != seg.Destination
                    || (seg.ClassOfService is not null && fare.ClassOfService != seg.ClassOfService))
                    throw new DomainException(ErrorCodes.InvalidKey, $"The fare for segment {seg.Origin}-{seg.Destination} is no longer valid.");
                response.Segments.Add(PricingEngine.PriceSegment(fare, seg.Ref, seg.Origin, seg.Destination, seg.International, paxTypes, snapshot));
            }
            return Envelope.Ok(response);
        });

        return app;
    }

    private static void EnsureCurrency(string? currencyCode, IConfiguration cfg)
    {
        var supported = cfg["Pricing:Currency"] ?? "USD";
        if (!string.Equals(currencyCode ?? supported, supported, StringComparison.OrdinalIgnoreCase))
            throw new DomainException(ErrorCodes.UnknownSellCurrencyCode,
                $"Currency '{currencyCode}' is not supported yet (Phase 1 sells in {supported} only).");
    }
}
