namespace Pss.Identity.Api.Application;

public class TokenRequest
{
    public string? ApplicationName { get; set; }
    public TokenCredentials? Credentials { get; set; }
}

public class TokenCredentials
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Domain { get; set; }
    public string? Location { get; set; }
}

/// <summary>Token response (blueprint §13.1).</summary>
public class TokenResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
    public int IdleTimeoutInMinutes { get; set; }
    public string? CultureCode { get; set; }
    public string? CurrencyCode { get; set; }
    public string? RoleCode { get; set; }
    public string? LocationCode { get; set; }
    public string? DomainCode { get; set; }
    public string? OrganizationCode { get; set; }
    public string? UserKey { get; set; }
    public string? PersonKey { get; set; }
    public bool MultiFactorRequired { get; set; }
}
