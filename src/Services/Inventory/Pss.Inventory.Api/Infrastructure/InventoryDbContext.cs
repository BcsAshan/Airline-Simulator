using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Persistence;
using Pss.Inventory.Api.Domain;

namespace Pss.Inventory.Api.Infrastructure;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<Leg> Legs => Set<Leg>();
    public DbSet<LegNest> LegNests => Set<LegNest>();
    public DbSet<LegClass> LegClasses => Set<LegClass>();
    public DbSet<InventoryHold> Holds => Set<InventoryHold>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyPssConventions();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Flight>(e =>
        {
            e.ToTable("Flight");
            e.HasKey(x => x.FlightId);
            e.Property(x => x.CarrierCode).HasMaxLength(3);
            e.Property(x => x.FlightNumber).HasMaxLength(4);
            e.Property(x => x.OpSuffix).HasMaxLength(1);
            e.Property(x => x.DepartureDateLocal).HasColumnType("date");
            e.HasIndex(x => new { x.CarrierCode, x.FlightNumber, x.OpSuffix, x.DepartureDateLocal }).IsUnique();
            e.HasMany(x => x.Legs).WithOne(x => x.Flight).HasForeignKey(x => x.FlightId);
        });
        b.Entity<Leg>(e =>
        {
            e.ToTable("Leg");
            e.HasKey(x => x.LegId);
            e.Property(x => x.Origin).HasMaxLength(3);
            e.Property(x => x.Destination).HasMaxLength(3);
            e.Property(x => x.RowVer).IsRowVersion();
            e.HasIndex(x => new { x.Origin, x.DepartureLocal });
            e.HasIndex(x => new { x.Destination, x.ArrivalLocal });
            e.HasMany(x => x.Nests).WithOne(x => x.Leg).HasForeignKey(x => x.LegId);
        });
        b.Entity<LegNest>(e =>
        {
            e.ToTable("LegNest");
            e.HasKey(x => x.LegNestId);
            e.HasMany(x => x.Classes).WithOne(x => x.Nest).HasForeignKey(x => x.LegNestId);
        });
        b.Entity<LegClass>(e =>
        {
            e.ToTable("LegClass");
            e.HasKey(x => x.LegClassId);
            e.Property(x => x.ClassOfService).HasMaxLength(2);
            e.Property(x => x.RowVer).IsRowVersion();
        });
        b.Entity<InventoryHold>(e =>
        {
            e.ToTable("InventoryHold");
            e.HasKey(x => x.HoldId);
            e.Property(x => x.SessionId).HasMaxLength(64);
            e.Property(x => x.RecordLocator).HasMaxLength(6);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => new { x.SessionId, x.Status });
            e.HasIndex(x => new { x.Status, x.ExpiresUtc });
        });
        // Phase 1: Inventory consumes events only (release is idempotent), so no outbox tables yet.
    }
}
