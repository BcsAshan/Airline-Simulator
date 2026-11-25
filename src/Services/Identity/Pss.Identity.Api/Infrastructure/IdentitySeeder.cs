using Microsoft.EntityFrameworkCore;
using Pss.Contracts.Booking;
using Pss.Identity.Api.Domain;
using Pss.SeedData;

namespace Pss.Identity.Api.Infrastructure;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IdentityDbContext db)
    {
        if (!await db.Users.AnyAsync()) await SeedUsersAsync(db);
        if (!await db.Applications.AnyAsync()) await SeedApplicationsAsync(db);
    }

    private static async Task SeedUsersAsync(IdentityDbContext db)
    {
        db.Roles.AddRange(
            new Role { RoleCode = "WWWA", Name = "Web anonymous", SettingsJson = """{"maxPassengers":9}""" },
            new Role { RoleCode = "WWWM", Name = "Web member", SettingsJson = """{"maxPassengers":9}""" },
            new Role { RoleCode = "AGNT", Name = "Call-centre agent", SettingsJson = """{"maxPassengers":30,"allowFeeOverride":true}""" });

        foreach (var u in DemoCarrier.Users)
        {
            var (hash, salt) = u.IsVirtual ? (string.Empty, string.Empty) : PasswordHasher.Hash(u.Password);
            db.Users.Add(new User
            {
                DomainCode = u.Domain,
                Username = u.Username,
                Name = u.Name,
                PasswordHash = hash,
                PasswordSalt = salt,
                UserType = (UserType)u.UserType,
                RoleCode = u.RoleCode,
                OrganizationCode = u.OrganizationCode,
                LocationCode = u.LocationCode,
                IsVirtual = u.IsVirtual,
                CreatedUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedApplicationsAsync(IdentityDbContext db)
    {
        var anonymous = await db.Users.FirstAsync(x => x.IsVirtual);
        // Blank application name = default ("Unknown" when the client sends nothing).
        db.Applications.AddRange(
            new ApplicationRegistration { ApplicationName = "", AnonymousUserId = anonymous.UserId, AllowedRetrieveStrategies = "lastName,firstAndLastName,email" },
            new ApplicationRegistration { ApplicationName = "dotREZWeb", AnonymousUserId = anonymous.UserId, AllowedRetrieveStrategies = "lastName,firstAndLastName,email" });
        await db.SaveChangesAsync();
    }
}
