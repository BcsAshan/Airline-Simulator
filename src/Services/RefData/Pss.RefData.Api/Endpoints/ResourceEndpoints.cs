using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Booking;
using Pss.RefData.Api.Infrastructure;

namespace Pss.RefData.Api.Endpoints;

/// <summary>
/// /resources/* and /settings/* (blueprint §7.2). Every response has a strong ETag; If-None-Match → 304.
/// Payloads are cached in memory for 24h. TODO(Phase 5): Redis cache + ReferenceDataChanged eviction + admin endpoints.
/// </summary>
public static class ResourceEndpoints
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public static WebApplication MapResourceEndpoints(this WebApplication app)
    {
        var v1 = app.MapGroup("/api/nsk/v1").WithTags("Resources").RequirePermission(PermissionLevel.Anonymous);
        var v2 = app.MapGroup("/api/nsk/v2").WithTags("Resources").RequirePermission(PermissionLevel.Anonymous);

        // Stations
        v1.MapGet("/resources/stations", (HttpContext http, RefDataDbContext db, IMemoryCache cache) =>
            Cached(http, cache, "stations", async () => (await db.Stations.AsNoTracking().OrderBy(s => s.StationCode).ToListAsync())
                .Select(s => new
                {
                    stationCode = s.StationCode, name = s.Name, shortName = s.ShortName, countryCode = s.CountryCode,
                    macCode = s.MacCode, currencyCode = s.CurrencyCode, timeZoneCode = s.TimeZoneId, inActive = s.InActive
                }).ToList()));

        v1.MapGet("/resources/stations/{stationCode}", async (string stationCode, HttpContext http, RefDataDbContext db) =>
        {
            var s = await db.Stations.AsNoTracking().FirstOrDefaultAsync(x => x.StationCode == stationCode.ToUpper());
            return s is null ? Envelope.NotFound() : ETagResults.OkWithETag(http, s);
        });

        // Markets (v2)
        v2.MapGet("/resources/markets", (HttpContext http, RefDataDbContext db, IMemoryCache cache) =>
            Cached(http, cache, "markets", async () => (await db.Markets.AsNoTracking().OrderBy(m => m.LocationCode).ThenBy(m => m.TravelLocationCode).ToListAsync())
                .Select(m => new
                {
                    locationCode = m.LocationCode, earliestCheckInFrom = 0, earliestCheckInTo = 0, inActive = m.InActive,
                    includesTaxesAndFees = 0, latestCheckInFrom = 0, latestCheckInTo = 0, locationType = 1,
                    travelLocationCode = m.TravelLocationCode, travelLocationType = 1, travelLocationsRequired = 0
                }).ToList()));

        // Countries (v2)
        v2.MapGet("/resources/countries", (HttpContext http, RefDataDbContext db, IMemoryCache cache) =>
            Cached(http, cache, "countries", async () => (await db.Countries.AsNoTracking().OrderBy(c => c.CountryCode).ToListAsync())
                .Select(ToCountryDto).ToList()));

        v2.MapGet("/resources/countries/{countryCode}", async (string countryCode, HttpContext http, RefDataDbContext db) =>
        {
            var c = await db.Countries.AsNoTracking().FirstOrDefaultAsync(x => x.CountryCode == countryCode.ToUpper());
            return c is null ? Envelope.NotFound() : ETagResults.OkWithETag(http, ToCountryDto(c));
        });

        // Currencies (v2)
        v2.MapGet("/resources/currencies", (HttpContext http, RefDataDbContext db, IMemoryCache cache) =>
            Cached(http, cache, "currencies", async () => (await db.Currencies.AsNoTracking().OrderBy(c => c.CurrencyCode).ToListAsync())
                .Select(c => new { currencyCode = c.CurrencyCode, description = c.Name, symbol = c.Symbol, decimalPlaces = c.DecimalPlaces, inActive = c.InActive })
                .ToList()));

        // Passenger types (v1)
        v1.MapGet("/resources/passengerTypes", (HttpContext http, RefDataDbContext db, IMemoryCache cache) =>
            Cached(http, cache, "passengerTypes", async () => (await db.PassengerTypes.AsNoTracking().OrderBy(p => p.PassengerTypeCode).ToListAsync())
                .Select(p => new
                {
                    nonAdult = p.NonAdult, passengerTypeCode = p.PassengerTypeCode, name = p.Name, allowed = p.Allowed,
                    inActive = p.InActive, typeIndicator = p.NonAdult ? 2 : 1, minimumAge = p.MinimumAge, maximumAge = p.MaximumAge
                }).ToList()));

        // Generic code lists: titles, documentTypes, contactTypes, nonBinaryGenders …
        v1.MapGet("/resources/{listName}", (string listName, HttpContext http, RefDataDbContext db, IMemoryCache cache) =>
            Cached(http, cache, "list:" + listName.ToLowerInvariant(), async () =>
            {
                var items = await db.CodeLists.AsNoTracking().Where(c => c.ListName == listName).OrderBy(c => c.Code).ToListAsync();
                if (items.Count == 0)
                    throw new DomainException(ErrorCodes.ResourceNotFound, $"The resource '{listName}' was not found.");
                return items.Select(c => new { code = c.Code, name = c.Name, inActive = c.InActive }).ToList();
            }));

        // Settings
        v1.MapGet("/settings/{**name}", (string name, HttpContext http, RefDataDbContext db, IMemoryCache cache) =>
            Cached(http, cache, "settings:" + name.ToLowerInvariant(), async () =>
            {
                var doc = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Name == name.ToLower());
                if (doc is null)
                    throw new DomainException(ErrorCodes.ResourceNotFound, $"The settings '{name}' were not found.");
                using var json = JsonDocument.Parse(doc.Json);
                return json.RootElement.Clone();
            }));

        // Utilities
        v1.MapGet("/utilities/stationLocalTime/{stationCode}", async (string stationCode, RefDataDbContext db, TimeProvider clock) =>
        {
            var s = await db.Stations.AsNoTracking().FirstOrDefaultAsync(x => x.StationCode == stationCode.ToUpper())
                    ?? throw new DomainException(ErrorCodes.InvalidStationCode, $"Unknown station '{stationCode}'.");
            var tz = TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId);
            var utc = clock.GetUtcNow().UtcDateTime;
            var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, tz), DateTimeKind.Unspecified);
            return Envelope.Ok(new { stationCode = s.StationCode, localTime = local, utcOffsetMinutes = (int)tz.GetUtcOffset(utc).TotalMinutes });
        }).WithTags("Utilities");

        return app;
    }

    private static object ToCountryDto(Domain.Country c) => new
    {
        countryCode = c.CountryCode, countryCode3C = c.CountryCode3C, cities = (object?)null, defaultCurrencyCode = c.DefaultCurrencyCode,
        hasProvinceStates = false, inActive = c.InActive, name = c.Name, provinceStates = (object?)null
    };

    private static async Task<IResult> Cached<T>(HttpContext http, IMemoryCache cache, string key, Func<Task<T>> factory)
    {
        var data = await cache.GetOrCreateAsync("refdata:" + key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await factory();
        });
        return ETagResults.OkWithETag(http, data);
    }
}
