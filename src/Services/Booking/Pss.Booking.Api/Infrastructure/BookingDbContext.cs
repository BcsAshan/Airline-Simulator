using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Messaging;
using Pss.BuildingBlocks.Persistence;

namespace Pss.Booking.Api.Infrastructure;

/// <summary>
/// Committed bookings (system of record). Phase 1 stores the aggregate as a JSON document plus indexed columns and
/// search projections. TODO(Phase 2): normalise into the §18 tables when reporting/manifest needs it.
/// </summary>
public class BookingRecord
{
    public long BookingId { get; set; }
    public string RecordLocator { get; set; } = string.Empty;
    public int Status { get; set; }
    public int PaidStatus { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal TotalAmount { get; set; }
    public decimal BalanceDue { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public DateTime? HoldExpirationUtc { get; set; }
    public DateTime? FirstDepartureUtc { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactLastName { get; set; }
    public string? CreatedAgentCode { get; set; }
    public string? ApplicationName { get; set; }
    public string Document { get; set; } = "{}";
    public byte[] RowVer { get; set; } = Array.Empty<byte>();
}

public class BookingPassengerIndex
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public string PassengerKey { get; set; } = string.Empty;
    public string PassengerTypeCode { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class BookingSegmentIndex
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateTime DepartureLocal { get; set; }
    public string ClassOfService { get; set; } = string.Empty;
}

public class BookingHistoryRecord
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string Event { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? AgentCode { get; set; }
}

public class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<BookingRecord> Bookings => Set<BookingRecord>();
    public DbSet<BookingPassengerIndex> PassengerIndex => Set<BookingPassengerIndex>();
    public DbSet<BookingSegmentIndex> SegmentIndex => Set<BookingSegmentIndex>();
    public DbSet<BookingHistoryRecord> History => Set<BookingHistoryRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyPssConventions();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BookingRecord>(e =>
        {
            e.ToTable("Booking");
            e.HasKey(x => x.BookingId);
            e.Property(x => x.RecordLocator).HasMaxLength(6).IsFixedLength();
            e.HasIndex(x => x.RecordLocator).IsUnique();
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.ContactEmail).HasMaxLength(256);
            e.Property(x => x.ContactLastName).HasMaxLength(64);
            e.Property(x => x.Document).HasColumnType("nvarchar(max)");
            e.Property(x => x.RowVer).IsRowVersion();
        });
        b.Entity<BookingPassengerIndex>(e =>
        {
            e.ToTable("BookingPassengerIndex");
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => x.LastName);
            e.Property(x => x.LastName).HasMaxLength(64);
            e.Property(x => x.FirstName).HasMaxLength(64);
        });
        b.Entity<BookingSegmentIndex>(e =>
        {
            e.ToTable("BookingSegmentIndex");
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => new { x.CarrierCode, x.FlightNumber, x.DepartureLocal });
        });
        b.Entity<BookingHistoryRecord>(e =>
        {
            e.ToTable("BookingHistory");
            e.HasIndex(x => x.BookingId);
        });
        b.AddPssOutboxEntities();
    }
}
