using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Booking;
using Pss.Contracts.Events;
using Pss.Identity.Api.Domain;
using Pss.Identity.Api.Infrastructure;

namespace Pss.Identity.Api.Application;

/// <summary>Token = handle to a server-side session (blueprint §5, §13.1, §19).</summary>
public sealed class TokenService(
    IdentityDbContext db,
    ISessionStore sessions,
    SigningKeyStore keys,
    IConfiguration configuration,
    IPublishEndpoint publisher,
    TimeProvider clock)
{
    private int IdleTimeoutMinutes => configuration.GetValue("Session:IdleTimeoutMinutes", 15);
    private string Issuer => configuration["Jwt:Issuer"] ?? "pss-identity";
    private TimeSpan TokenLifetime => TimeSpan.FromHours(configuration.GetValue("Jwt:LifetimeHours", 24));

    public async Task<TokenResponse> CreateAnonymousAsync(string? applicationName, CancellationToken ct)
    {
        var appName = string.IsNullOrWhiteSpace(applicationName) ? string.Empty : applicationName.Trim();
        var registration = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.ApplicationName == appName, ct)
                           ?? await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.ApplicationName == string.Empty, ct)
                           ?? throw new DomainException(ErrorCodes.InvalidApplicationName, "No anonymous user is configured for this application.");

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == registration.AnonymousUserId && u.IsActive, ct)
                   ?? throw new DomainException(ErrorCodes.InvalidApplicationName, "The configured anonymous user is not active.");

        return await StartSessionAsync(user, string.IsNullOrEmpty(appName) ? "Unknown" : appName, ct);
    }

    public async Task<TokenResponse> CreateUserAsync(TokenCredentials credentials, string? applicationName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrWhiteSpace(credentials.Password))
            throw new DomainException(ErrorCodes.NullRequest, "Username and password are required.");

        var domain = string.IsNullOrWhiteSpace(credentials.Domain) ? "DEF" : credentials.Domain.Trim().ToUpperInvariant();
        var username = credentials.Username.Trim();
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.DomainCode == domain && u.Username == username, ct);

        // Uniform failure (no user enumeration). Virtual (anonymous) users can never log on interactively.
        if (user is null || !user.IsActive || user.IsVirtual || !PasswordHasher.Verify(credentials.Password, user.PasswordHash, user.PasswordSalt))
            throw DomainException.Unauthorized("Invalid credentials.", ErrorCodes.InvalidCredentials);

        if (user.PasswordExpired)
            throw new DomainException(ErrorCodes.PasswordExpired, "The password has expired and must be changed.");

        if (!string.IsNullOrWhiteSpace(credentials.Location)) user.LocationCode = credentials.Location.Trim().ToUpperInvariant();
        return await StartSessionAsync(user, string.IsNullOrWhiteSpace(applicationName) ? "Unknown" : applicationName.Trim(), ct);
    }

    public TokenResponse Refresh(SessionInfo session) => Issue(session);

    public async Task LogoutAsync(SessionInfo session, CancellationToken ct)
    {
        await sessions.RemoveAsync(session.SessionId);
        // Releases inventory holds / drops the booking draft in other services.
        await publisher.Publish(new SessionExpired(session.SessionId, "Logout", clock.GetUtcNow().UtcDateTime), ct);
    }

    private async Task<TokenResponse> StartSessionAsync(User user, string applicationName, CancellationToken ct)
    {
        var session = new SessionInfo
        {
            SessionId = Guid.NewGuid().ToString("N"),
            UserType = user.UserType,
            AgentId = user.UserId,
            AgentCode = user.Username,
            RoleCode = user.RoleCode,
            DomainCode = user.DomainCode,
            OrganizationCode = user.OrganizationCode,
            LocationCode = user.LocationCode,
            ApplicationName = applicationName,
            ChannelType = ChannelType.DigitalApi,
            CultureCode = configuration["Session:DefaultCulture"] ?? "en-US",
            CurrencyCode = configuration["Session:DefaultCurrency"] ?? "USD",
            PersonKey = user.PersonKey,
            UserKey = user.UserId.ToString(),
            IdleTimeoutMinutes = IdleTimeoutMinutes,
            CreatedUtc = clock.GetUtcNow().UtcDateTime
        };

        await sessions.CreateAsync(session);
        await publisher.Publish(new SessionCreated(session.SessionId, (int)session.UserType, session.AgentCode,
            session.ApplicationName, session.CreatedUtc), ct);
        return Issue(session);
    }

    private TokenResponse Issue(SessionInfo s)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.Add(TokenLifetime);
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            [JwtRegisteredClaimNames.Sub] = s.AgentCode,
            [SessionClaims.SessionId] = s.SessionId,
            [SessionClaims.UserType] = ((int)s.UserType).ToString(),
            [SessionClaims.RoleCode] = s.RoleCode,
            [SessionClaims.AgentId] = s.AgentId.ToString(),
            [SessionClaims.AgentName] = s.AgentCode,
            [SessionClaims.DomainCode] = s.DomainCode,
            [SessionClaims.OrganizationCode] = s.OrganizationCode,
            [SessionClaims.LocationCode] = s.LocationCode,
            [SessionClaims.ApplicationName] = s.ApplicationName,
            [SessionClaims.ChannelType] = ((int)s.ChannelType).ToString(),
            [SessionClaims.CultureCode] = s.CultureCode,
            [SessionClaims.CurrencyCode] = s.CurrencyCode,
            [SessionClaims.UserKey] = s.UserKey ?? string.Empty
        };
        if (!string.IsNullOrEmpty(s.PersonKey)) claims[SessionClaims.PersonKey] = s.PersonKey;

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Claims = claims,
            SigningCredentials = keys.Credentials
        });

        return new TokenResponse
        {
            Token = token,
            Expires = expires,
            IdleTimeoutInMinutes = s.IdleTimeoutMinutes,
            CultureCode = s.CultureCode,
            CurrencyCode = s.CurrencyCode,
            RoleCode = s.RoleCode,
            LocationCode = s.LocationCode,
            DomainCode = s.DomainCode,
            OrganizationCode = s.OrganizationCode,
            UserKey = s.UserKey,
            PersonKey = s.PersonKey,
            MultiFactorRequired = false
        };
    }
}
