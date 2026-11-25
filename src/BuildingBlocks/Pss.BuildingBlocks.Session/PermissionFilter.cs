using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.Contracts.Booking;

namespace Pss.BuildingBlocks.Session;

public static class PermissionExtensions
{
    private const string SessionItemKey = "pss.session";

    /// <summary>
    /// Requires a valid token AND a live server-side session (token alone is not enough — blueprint §19),
    /// slides the idle timeout and enforces the permission level. Failure = 401 envelope (Navitaire-compatible).
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, PermissionLevel level)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var sid = http.User.FindFirstValue(SessionClaims.SessionId);
            if (http.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(sid))
                throw DomainException.Unauthorized();

            var store = http.RequestServices.GetRequiredService<ISessionStore>();
            var sw = Stopwatch.StartNew();
            var session = await store.TouchAsync(sid);
            http.RequestServices.GetService<ServerTimingCollector>()?.Add("session", sw.Elapsed);

            if (session is null) throw DomainException.Unauthorized("The session has expired or was logged out.");
            if (!Satisfies(session.UserType, level)) throw DomainException.AgentOnly();

            http.Items[SessionItemKey] = session;
            return await next(context);
        });
        return builder;
    }

    public static bool Satisfies(UserType userType, PermissionLevel level) => level switch
    {
        PermissionLevel.Anonymous => true,
        PermissionLevel.Customer => userType is UserType.Customer or UserType.Agent,
        PermissionLevel.Agent => userType == UserType.Agent,
        _ => false
    };

    /// <summary>The session validated by <see cref="RequirePermission{TBuilder}"/>.</summary>
    public static SessionInfo GetSession(this HttpContext http)
        => http.Items.TryGetValue(SessionItemKey, out var s) && s is SessionInfo session
            ? session
            : throw DomainException.Unauthorized();
}
