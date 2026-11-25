using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Persistence;
using Pss.Pricing.Api.Domain;
using Pss.SeedData;

namespace Pss.Pricing.Api.Infrastructure;

public class PricingDbContext(DbContextOptions<PricingDbContext> options) : DbContext(options)
{
    public DbSet<Fare> Fares => Set<Fare>();
    public DbSet<PassengerTypeDiscount> PassengerTypeDiscounts => Set<PassengerTypeDiscount>();
    public DbSet<TaxDefinition> Taxes => Set<TaxDefinition>();
    public DbSet<StationCountry> StationCountries => Set<StationCountry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyPssConventions();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Fare>(e =>
        {
            e.ToTable("Fare");
            e.HasKey(x => x.FareId);
            e.HasIndex(x => new { x.CarrierCode, x.Origin, x.Destination, x.ClassOfService });
            e.Property(x => x.FareBasisCode).HasMaxLength(16);
        });
        b.Entity<PassengerTypeDiscount>(e => { e.ToTable("PassengerTypeDiscount"); e.HasKey(x => x.PassengerTypeCode); });
        b.Entity<TaxDefinition>(e => { e.ToTable("TaxDefinition"); e.HasKey(x => x.TaxId); });
        b.Entity<StationCountry>(e => { e.ToTable("StationCountry"); e.HasKey(x => x.StationCode); });
    }

    public static async Task SeedAsync(PricingDbContext db)
    {
        if (await db.Fares.AnyAsync()) return;

        foreach (var market in DemoCarrier.MarketFares)
        foreach (var (origin, destination) in new[] { (market.Origin, market.Destination), (market.Destination, market.Origin) })
        {
            var seq = 0;
            foreach (var fc in DemoCarrier.FareClasses)
            {
                db.Fares.Add(new Fare
                {
                    CarrierCode = DemoCarrier.CarrierCode,
                    Origin = origin,
                    Destination = destination,
                    ClassOfService = fc.ClassOfService,
                    FareBasisCode = $"{fc.ClassOfService}{fc.ProductClass}OW",
                    RuleNumber = fc.ProductClass + "01",
                    ProductClass = fc.ProductClass,
                    TravelClassCode = fc.Cabin,
                    FareSequence = ++seq,
                    Amount = Math.Round(market.EconomyBase * fc.Multiplier, 0, MidpointRounding.AwayFromZero),
                    CurrencyCode = DemoCarrier.DefaultCurrency
                });
            }
        }

        db.PassengerTypeDiscounts.AddRange(DemoCarrier.PassengerTypes.Select(p =>
            new PassengerTypeDiscount { PassengerTypeCode = p.Code, DiscountPercent = p.FareDiscountPercent }));

        db.Taxes.AddRange(DemoCarrier.Taxes.Select(t => new TaxDefinition
        {
            Code = t.Code, Name = t.Name, DepartureCountryCode = t.DepartureCountry, Scope = t.Scope, ChargeType = t.ChargeType,
            Amount = t.Amount, IsPercent = t.IsPercent, CurrencyCode = DemoCarrier.DefaultCurrency,
            ExemptPassengerTypes = string.Join(',', t.ExemptPassengerTypes)
        }));

        db.StationCountries.AddRange(DemoCarrier.Stations.Select(s => new StationCountry { StationCode = s.Code, CountryCode = s.CountryCode }));
        await db.SaveChangesAsync();
    }
}
