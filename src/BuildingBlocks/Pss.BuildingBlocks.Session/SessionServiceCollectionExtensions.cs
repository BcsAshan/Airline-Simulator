using System.Net.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Pss.BuildingBlocks.Api;
using StackExchange.Redis;

namespace Pss.BuildingBlocks.Session;

public static class SessionServiceCollectionExtensions
{
    /// <summary>Redis connection + session store + session lock.</summary>
    public static IServiceCollection AddPssRedisSessions(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<ISessionStore, RedisSessionStore>();
        services.AddSingleton<ISessionLock, RedisSessionLock>();
        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: new[] { "ready" });
        return services;
    }

    /// <summary>
    /// JWT bearer validation (RS256 tokens from Identity via JWKS). Also accepts a raw token without the
    /// "Bearer " prefix, as dotREZ does. Pass <paramref name="signingKey"/> inside Identity itself.
    /// </summary>
    public static IServiceCollection AddPssAuthentication(this IServiceCollection services, IConfiguration configuration, SecurityKey? signingKey = null)
    {
        var issuer = configuration["Jwt:Issuer"] ?? "pss-identity";
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = false;
                if (signingKey is null)
                {
                    var identityUrl = (configuration["Services:Identity"] ?? "http://localhost:5101").TrimEnd('/');
                    o.MetadataAddress = identityUrl + "/.well-known/openid-configuration";
                }

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    IssuerSigningKey = signingKey,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = SessionClaims.AgentName
                };

                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var header = ctx.Request.Headers.Authorization.ToString();
                        if (!string.IsNullOrWhiteSpace(header) && !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                            ctx.Token = header.Trim();
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();
        return services;
    }

    /// <summary>Typed HttpClient for an internal service, forwarding the caller's token and correlation id.</summary>
    public static IHttpClientBuilder AddInternalServiceClient<TClient, TImplementation>(this IServiceCollection services,
        IConfiguration configuration, string serviceName, string defaultUrl)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.AddTransient<ForwardSessionHandler>();
        return services.AddHttpClient<TClient, TImplementation>(c =>
            {
                c.BaseAddress = new Uri((configuration[$"Services:{serviceName}"] ?? defaultUrl).TrimEnd('/') + "/");
                c.Timeout = TimeSpan.FromSeconds(configuration.GetValue("Services:TimeoutSeconds", 30));
            })
            .AddHttpMessageHandler<ForwardSessionHandler>();
    }

    public static WebApplication UsePssAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}

/// <summary>Copies Authorization + X-Correlation-Id from the inbound request to internal calls.</summary>
public sealed class ForwardSessionHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext;
        if (http is not null)
        {
            var auth = http.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(auth) && !request.Headers.Contains("Authorization"))
                request.Headers.TryAddWithoutValidation("Authorization", auth);

            var correlation = http.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
            if (!string.IsNullOrWhiteSpace(correlation))
                request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, correlation);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
