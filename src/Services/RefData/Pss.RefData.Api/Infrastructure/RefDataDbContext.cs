using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Persistence;
using Pss.RefData.Api.Domain;
using Pss.SeedData;

namespace Pss.RefData.Api.Infrastructure;

public class RefDataDbContext(DbContextOptions<RefDataDbContext> options) : DbContext(options)
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Market> Markets => Set<Market>();
    public DbSet<PassengerType> PassengerTypes => Set<PassengerType>();
    public DbSet<CodeListItem> CodeLists => Set<CodeListItem>();
    public DbSet<SettingDocument> Settings => Set<SettingDocument>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyPssConventions();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Station>(e => { e.ToTable("Station"); e.HasKey(x => x.StationCode); e.Property(x => x.StationCode).HasMaxLength(3); });
        b.Entity<Country>(e => { e.ToTable("Country"); e.HasKey(x => x.CountryCode); e.Property(x => x.CountryCode).HasMaxLength(2); });
        b.Entity<Currency>(e => { e.ToTable("Currency"); e.HasKey(x => x.CurrencyCode); e.Property(x => x.CurrencyCode).HasMaxLength(3); });
        b.Entity<Market>(e => { e.ToTable("Market"); e.HasIndex(x => new { x.LocationCode, x.TravelLocationCode }).IsUnique(); });
        b.Entity<PassengerType>(e => { e.ToTable("PassengerType"); e.HasKey(x => x.PassengerTypeCode); e.Property(x => x.PassengerTypeCode).HasMaxLength(4); });
        b.Entity<CodeListItem>(e => { e.ToTable("CodeListItem"); e.HasIndex(x => new { x.ListName, x.Code }).IsUnique(); e.Property(x => x.ListName).HasMaxLength(64); e.Property(x => x.Code).HasMaxLength(16); });
        b.Entity<SettingDocument>(e => { e.ToTable("SettingDocument"); e.HasKey(x => x.Name); e.Property(x => x.Name).HasMaxLength(128); });
    }

    public static async Task SeedAsync(RefDataDbContext db)
    {
        if (await db.Stations.AnyAsync()) return;

        db.Stations.AddRange(DemoCarrier.Stations.Select(s => new Station
        {
            StationCode = s.Code, Name = s.Name, ShortName = s.Name, CountryCode = s.CountryCode,
            MacCode = s.MacCode, TimeZoneId = s.TimeZoneId, CurrencyCode = s.CurrencyCode
        }));
        db.Countries.AddRange(DemoCarrier.Countries.Select(c => new Country
        {
            CountryCode = c.Code, CountryCode3C = c.Code3, Name = c.Name, DefaultCurrencyCode = c.CurrencyCode
        }));
        db.Currencies.AddRange(DemoCarrier.Currencies.Select(c => new Currency
        {
            CurrencyCode = c.Code, Name = c.Name, Symbol = c.Symbol, DecimalPlaces = c.DecimalPlaces
        }));
        foreach (var m in DemoCarrier.MarketFares)
        {
            db.Markets.Add(new Market { LocationCode = m.Origin, TravelLocationCode = m.Destination });
            db.Markets.Add(new Market { LocationCode = m.Destination, TravelLocationCode = m.Origin });
        }
        db.PassengerTypes.AddRange(DemoCarrier.PassengerTypes.Select(p => new PassengerType
        {
            PassengerTypeCode = p.Code, Name = p.Name, MinimumAge = p.MinimumAge, MaximumAge = p.MaximumAge, NonAdult = p.NonAdult
        }));
        db.CodeLists.AddRange(DemoCarrier.CodeLists.Select(c => new CodeListItem { ListName = c.ListName, Code = c.Code, Name = c.Name }));
        db.Settings.AddRange(DemoCarrier.Settings.Select(s => new SettingDocument { Name = s.Name, Json = s.Json }));
        await db.SaveChangesAsync();
    }
}
