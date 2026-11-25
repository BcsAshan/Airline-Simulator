using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Messaging;
using Pss.BuildingBlocks.Persistence;
using Pss.Payment.Api.Domain;

namespace Pss.Payment.Api.Infrastructure;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();
    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyPssConventions();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PaymentRecord>(e =>
        {
            e.ToTable("Payment");
            e.HasKey(x => x.PaymentId);
            e.HasIndex(x => x.BookingId);
            e.Property(x => x.MethodCode).HasMaxLength(4);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.AccountNumberMasked).HasMaxLength(32);
            e.Property(x => x.RowVer).IsRowVersion();
        });
        b.Entity<PaymentTransaction>(e =>
        {
            e.ToTable("PaymentTransaction");
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.PaymentId);
            e.Property(x => x.IdempotencyKey).HasMaxLength(128);
        });
        b.AddPssOutboxEntities();
    }
}
