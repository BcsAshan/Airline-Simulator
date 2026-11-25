using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Pss.Pricing.Api.Domain;

namespace Pss.Pricing.Api.Infrastructure;

/// <summary>Loads fares/taxes/discounts into an in-memory snapshot (refreshed every few minutes).</summary>
public sealed class PricingSnapshotProvider(IServiceScopeFactory scopes, IMemoryCache cache, IConfiguration configuration)
{
    private const string CacheKey = "pricing:snapshot";

    public async Task<PricingSnapshot> GetAsync(CancellationToken ct)
    {
        var snapshot = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(configuration.GetValue("Pricing:SnapshotMinutes", 5));
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            return new PricingSnapshot
            {
                Fares = await db.Fares.AsNoTracking().ToListAsync(ct),
                Taxes = await db.Taxes.AsNoTracking().ToListAsync(ct),
                PassengerTypeDiscounts = await db.PassengerTypeDiscounts.AsNoTracking().ToDictionaryAsync(d => d.PassengerTypeCode, d => d.DiscountPercent, ct),
                StationCountries = await db.StationCountries.AsNoTracking().ToDictionaryAsync(s => s.StationCode, s => s.CountryCode, ct)
            };
        });
        return snapshot!;
    }

    public void Invalidate() => cache.Remove(CacheKey);
}
