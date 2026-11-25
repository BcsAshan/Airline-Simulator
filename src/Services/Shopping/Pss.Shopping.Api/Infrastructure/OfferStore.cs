using System.Text.Json;
using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Keys;
using Pss.Contracts.Internal;
using StackExchange.Redis;

namespace Pss.Shopping.Api.Infrastructure;

/// <summary>Offer store (blueprint §7.5): every fareAvailabilityKey resolves to the journey + fares that were shown.</summary>
public sealed class OfferStore(IConnectionMultiplexer redis, IConfiguration configuration)
{
    private TimeSpan Ttl => TimeSpan.FromMinutes(configuration.GetValue("Shopping:OfferTtlMinutes", 30));
    private static string Key(string offerId) => $"pss:offer:{offerId}";

    public Task SaveAsync(string offerId, Dictionary<int, OfferJourneyFare> entries)
        => redis.GetDatabase().StringSetAsync(Key(offerId), JsonSerializer.Serialize(entries, PssJson.Options), Ttl);

    public async Task<OfferJourneyFare> GetAsync(string fareAvailabilityKey)
    {
        var key = FareAvailabilityKey.Parse(fareAvailabilityKey);
        var value = await redis.GetDatabase().StringGetAsync(Key(key.OfferId));
        if (value.IsNullOrEmpty)
            throw new DomainException(ErrorCodes.InvalidKey, "The fare availability key has expired; please search again.");

        var entries = JsonSerializer.Deserialize<Dictionary<int, OfferJourneyFare>>(value.ToString(), PssJson.Options);
        if (entries is null || !entries.TryGetValue(key.Index, out var entry)) throw DomainException.InvalidKey(fareAvailabilityKey);
        return entry;
    }
}
