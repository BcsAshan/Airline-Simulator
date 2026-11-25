using Microsoft.EntityFrameworkCore;
using Pss.Inventory.Api.Domain;
using Pss.SeedData;

namespace Pss.Inventory.Api.Infrastructure;

/// <summary>Generates N days of dated flights for carrier XX from the daily schedule (station-local times → UTC).</summary>
public static class InventorySeeder
{
    public static async Task SeedAsync(InventoryDbContext db, IConfiguration configuration, TimeProvider clock)
    {
        if (await db.Flights.AnyAsync()) return;

        var days = configuration.GetValue("Seed:Days", 30);
        var start = clock.GetUtcNow().UtcDateTime.Date;
        var equipment = DemoCarrier.Equipments.ToDictionary(e => e.Type);

        for (var d = 0; d < days; d++)
        {
            var date = start.AddDays(d);
            foreach (var sf in DemoCarrier.Schedule)
            {
                var eq = equipment[sf.Equipment];
                var originTz = DemoCarrier.TimeZoneFor(sf.Origin);
                var destTz = DemoCarrier.TimeZoneFor(sf.Destination);

                var depLocal = DateTime.SpecifyKind(date.Add(sf.DepartureLocal), DateTimeKind.Unspecified);
                var depUtc = TimeZoneInfo.ConvertTimeToUtc(depLocal, originTz);
                var arrUtc = depUtc.Add(sf.BlockTime);
                var arrLocal = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(arrUtc, destTz), DateTimeKind.Unspecified);
                var capacity = eq.BusinessSeats + eq.EconomySeats;

                var leg = new Leg
                {
                    Seq = 0,
                    Origin = sf.Origin,
                    Destination = sf.Destination,
                    DepartureLocal = depLocal,
                    ArrivalLocal = arrLocal,
                    DepartureUtc = depUtc,
                    ArrivalUtc = arrUtc,
                    DepartureTimeVariant = (int)originTz.GetUtcOffset(depUtc).TotalMinutes,
                    ArrivalTimeVariant = (int)destTz.GetUtcOffset(arrUtc).TotalMinutes,
                    EquipmentType = eq.Type,
                    EquipmentSuffix = string.IsNullOrEmpty(eq.Suffix) ? null : eq.Suffix,
                    PrbcCode = eq.PrbcCode,
                    Capacity = capacity,
                    AdjustedCapacity = capacity,
                    Lid = capacity,
                    ETicket = true,
                    International = DemoCarrier.CountryOf(sf.Origin) != DemoCarrier.CountryOf(sf.Destination)
                };

                foreach (var cabin in new[] { ("C", eq.BusinessSeats), ("Y", eq.EconomySeats) })
                {
                    if (cabin.Item2 <= 0) continue;
                    var nest = new LegNest { ClassNest = cabin.Item1, TravelClassCode = cabin.Item1, Lid = cabin.Item2, AdjustedCapacity = cabin.Item2 };
                    foreach (var fc in DemoCarrier.FareClasses.Where(c => c.Cabin == cabin.Item1))
                    {
                        nest.Classes.Add(new LegClass
                        {
                            ClassOfService = fc.ClassOfService,
                            ClassRank = fc.Rank,
                            AuthorizedUnits = (int)Math.Ceiling(cabin.Item2 * fc.AuthorizedShare),
                            Status = 1
                        });
                    }
                    leg.Nests.Add(nest);
                }

                db.Flights.Add(new Flight
                {
                    CarrierCode = DemoCarrier.CarrierCode,
                    FlightNumber = sf.FlightNumber,
                    DepartureDateLocal = depLocal.Date,
                    Legs = { leg }
                });
            }
            await db.SaveChangesAsync();
        }
    }
}
