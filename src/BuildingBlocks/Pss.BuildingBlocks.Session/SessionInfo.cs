using Pss.Contracts.Booking;

namespace Pss.BuildingBlocks.Session;

/// <summary>JWT claim names issued by the Identity service (blueprint §13.1 / §19).</summary>
public static class SessionClaims
{
    public const string SessionId = "sid";
    public const string UserType = "userType";
    public const string RoleCode = "roleCode";
    public const string AgentId = "agentId";
    public const string AgentName = "agentName";
    public const string DomainCode = "domainCode";
    public const string OrganizationCode = "organizationCode";
    public const string LocationCode = "locationCode";
    public const string ApplicationName = "clientName";
    public const string ChannelType = "channelType";
    public const string CultureCode = "cultureCode";
    public const string CurrencyCode = "currencyCode";
    public const string PersonKey = "personKey";
    public const string UserKey = "userKey";
}

/// <summary>Server-side session (Redis key pss:session:{sid}). The token is only a handle to this.</summary>
public sealed class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public UserType UserType { get; set; } = UserType.Anonymous;
    public long AgentId { get; set; }
    public string AgentCode { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
    public string OrganizationCode { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public ChannelType ChannelType { get; set; } = ChannelType.DigitalApi;
    public string CultureCode { get; set; } = "en-US";
    public string CurrencyCode { get; set; } = "USD";
    public string? PersonKey { get; set; }
    public string? UserKey { get; set; }
    public int IdleTimeoutMinutes { get; set; } = 15;
    public DateTime CreatedUtc { get; set; }

    public bool IsAgent => UserType == UserType.Agent;

    public PointOfSale ToPointOfSale() => new()
    {
        AgentCode = AgentCode,
        DomainCode = DomainCode,
        LocationCode = LocationCode,
        OrganizationCode = OrganizationCode
    };

    public SourcePointOfSale ToSourcePointOfSale() => new()
    {
        AgentCode = AgentCode,
        DomainCode = DomainCode,
        LocationCode = LocationCode,
        OrganizationCode = OrganizationCode
    };
}

/// <summary>Redis key conventions shared by all services.</summary>
public static class SessionKeys
{
    public const string ExpiryIndex = "pss:sessions:expiry";
    public static string Session(string sid) => $"pss:session:{sid}";
    public static string Lock(string sid) => $"pss:session:{sid}:lock";
    public static string BookingDraft(string sid) => $"pss:session:{sid}:booking";
}
