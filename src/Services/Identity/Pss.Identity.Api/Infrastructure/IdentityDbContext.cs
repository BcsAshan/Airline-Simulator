using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Persistence;
using Pss.Identity.Api.Domain;

namespace Pss.Identity.Api.Infrastructure;

public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<ApplicationRegistration> Applications => Set<ApplicationRegistration>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyPssConventions();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("User");
            e.HasKey(x => x.UserId);
            e.HasIndex(x => new { x.DomainCode, x.Username }).IsUnique();
            e.Property(x => x.DomainCode).HasMaxLength(10);
            e.Property(x => x.Username).HasMaxLength(128);
            e.Property(x => x.RoleCode).HasMaxLength(10);
            e.Property(x => x.OrganizationCode).HasMaxLength(10);
            e.Property(x => x.LocationCode).HasMaxLength(10);
            e.Property(x => x.UserType).HasConversion<int>();
        });
        b.Entity<Role>(e =>
        {
            e.ToTable("Role");
            e.HasKey(x => x.RoleCode);
            e.Property(x => x.RoleCode).HasMaxLength(10);
        });
        b.Entity<ApplicationRegistration>(e =>
        {
            e.ToTable("ApplicationRegistration");
            e.HasKey(x => x.ApplicationName);
            e.Property(x => x.ApplicationName).HasMaxLength(64);
        });
    }
}
