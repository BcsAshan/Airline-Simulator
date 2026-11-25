using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Booking;
using Pss.Identity.Api.Application;
using Pss.Identity.Api.Infrastructure;

namespace Pss.Identity.Api.Endpoints;

public static class TokenEndpoints
{
    public static WebApplication MapTokenEndpoints(this WebApplication app)
    {
        // ---- /api/auth/v1/token (current) ----
        var auth = app.MapGroup("/api/auth/v1/token").WithTags("Token");

        auth.MapPost("/anonymous", async (TokenRequest? request, TokenService tokens, CancellationToken ct)
            => Envelope.Created(null, await tokens.CreateAnonymousAsync(request?.ApplicationName, ct)));

        auth.MapPost("/user", async (TokenRequest? request, TokenService tokens, CancellationToken ct) =>
        {
            if (request?.Credentials is null)
                throw new Pss.BuildingBlocks.Domain.DomainException(Pss.BuildingBlocks.Domain.ErrorCodes.NullRequest, "Credentials are required.");
            return Envelope.Created(null, await tokens.CreateUserAsync(request.Credentials, request.ApplicationName, ct));
        });

        auth.MapGet("", (HttpContext http) => Envelope.Ok(TokenInfo.From(http.GetSession())))
            .RequirePermission(PermissionLevel.Anonymous);

        auth.MapPut("", (HttpContext http, TokenService tokens) => Envelope.Ok(tokens.Refresh(http.GetSession())))
            .RequirePermission(PermissionLevel.Anonymous);

        auth.MapDelete("", async (HttpContext http, TokenService tokens, CancellationToken ct) =>
        {
            await tokens.LogoutAsync(http.GetSession(), ct);
            return Envelope.OkNoData();
        }).RequirePermission(PermissionLevel.Anonymous);

        // ---- legacy /api/nsk/v{1|2}/token (used by the Navitaire Postman collections and HAR files) ----
        foreach (var v in new[] { "v1", "v2" })
        {
            var legacy = app.MapGroup($"/api/nsk/{v}/token").WithTags("Token (legacy)");
            legacy.MapPost("", async (TokenRequest? request, TokenService tokens, CancellationToken ct) =>
            {
                var result = request?.Credentials is { Username: not null }
                    ? await tokens.CreateUserAsync(request.Credentials, request.ApplicationName, ct)
                    : await tokens.CreateAnonymousAsync(request?.ApplicationName, ct);
                return Envelope.Created(null, new { token = result.Token, idleTimeoutInMinutes = result.IdleTimeoutInMinutes });
            });
            legacy.MapGet("", (HttpContext http) => Envelope.Ok(TokenInfo.From(http.GetSession())))
                .RequirePermission(PermissionLevel.Anonymous);
            legacy.MapPut("", (HttpContext http, TokenService tokens) => Envelope.Ok(tokens.Refresh(http.GetSession())))
                .RequirePermission(PermissionLevel.Anonymous);
            legacy.MapDelete("", async (HttpContext http, TokenService tokens, CancellationToken ct) =>
            {
                await tokens.LogoutAsync(http.GetSession(), ct);
                return Envelope.OkNoData();
            }).RequirePermission(PermissionLevel.Anonymous);
        }

        return app;
    }

    public static WebApplication MapWellKnownEndpoints(this WebApplication app)
    {
        app.MapGet("/.well-known/openid-configuration", (HttpContext http, IConfiguration cfg) =>
        {
            var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            return Results.Json(new Dictionary<string, object>
            {
                ["issuer"] = cfg["Jwt:Issuer"] ?? "pss-identity",
                ["jwks_uri"] = baseUrl + "/.well-known/jwks.json",
                ["id_token_signing_alg_values_supported"] = new[] { "RS256" }
            });
        }).WithTags("Discovery");

        app.MapGet("/.well-known/jwks.json", (SigningKeyStore keys) => Results.Json(new
        {
            keys = new[]
            {
                new { kty = "RSA", use = "sig", alg = "RS256", kid = keys.KeyId, n = keys.Modulus, e = keys.Exponent }
            }
        })).WithTags("Discovery");

        return app;
    }
}

/// <summary>GET token: session information (never the password or internals).</summary>
public sealed record TokenInfo(string SessionId, int UserType, string AgentCode, string RoleCode, string DomainCode,
    string OrganizationCode, string LocationCode, string ApplicationName, string CultureCode, string CurrencyCode,
    int IdleTimeoutInMinutes, string? UserKey, string? PersonKey)
{
    public static TokenInfo From(SessionInfo s) => new(s.SessionId, (int)s.UserType, s.AgentCode, s.RoleCode, s.DomainCode,
        s.OrganizationCode, s.LocationCode, s.ApplicationName, s.CultureCode, s.CurrencyCode, s.IdleTimeoutMinutes, s.UserKey, s.PersonKey);
}
