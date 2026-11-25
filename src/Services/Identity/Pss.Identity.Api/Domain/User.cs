using Pss.Contracts.Booking;

namespace Pss.Identity.Api.Domain;

public class User
{
    public long UserId { get; set; }
    public string DomainCode { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public UserType UserType { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string OrganizationCode { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public string? PersonKey { get; set; }
    /// <summary>Virtual users (anonymous) cannot log on interactively and have no concurrent-session limit.</summary>
    public bool IsVirtual { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Agent-set/reset passwords are expired (nsk-server:PasswordExpired on next logon).</summary>
    public bool PasswordExpired { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class Role
{
    public string RoleCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Role settings JSON (required fields, overrides, check-in window …) — blueprint §19.</summary>
    public string SettingsJson { get; set; } = "{}";
}

/// <summary>applicationName → anonymous user mapping (Navitaire anonymousUserOptions).</summary>
public class ApplicationRegistration
{
    public string ApplicationName { get; set; } = string.Empty;
    public long AnonymousUserId { get; set; }
    public string? AllowedRetrieveStrategies { get; set; }
}
