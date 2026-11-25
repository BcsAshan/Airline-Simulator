using System.Text.Json;
using Pss.BuildingBlocks.Api;
using StackExchange.Redis;

namespace Pss.BuildingBlocks.Session;

public interface ISessionStore
{
    Task CreateAsync(SessionInfo session);
    Task<SessionInfo?> GetAsync(string sessionId);
    /// <summary>Returns the session and slides its idle timeout; null when expired/unknown.</summary>
    Task<SessionInfo?> TouchAsync(string sessionId);
    Task<bool> RemoveAsync(string sessionId);
}

/// <summary>
/// Sessions live in Redis with a sliding TTL. A sorted set (pss:sessions:expiry) tracks expected expiry so the
/// Identity sweeper can publish SessionExpired (which releases inventory holds) — blueprint §5.
/// </summary>
public sealed class RedisSessionStore(IConnectionMultiplexer redis) : ISessionStore
{
    private IDatabase Db => redis.GetDatabase();

    public async Task CreateAsync(SessionInfo session)
    {
        var ttl = TimeSpan.FromMinutes(session.IdleTimeoutMinutes);
        var json = JsonSerializer.Serialize(session, PssJson.Options);
        await Db.StringSetAsync(SessionKeys.Session(session.SessionId), json, ttl);
        await Db.SortedSetAddAsync(SessionKeys.ExpiryIndex, session.SessionId, ExpiryScore(ttl));
    }

    public async Task<SessionInfo?> GetAsync(string sessionId)
    {
        var value = await Db.StringGetAsync(SessionKeys.Session(sessionId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<SessionInfo>(value.ToString(), PssJson.Options);
    }

    public async Task<SessionInfo?> TouchAsync(string sessionId)
    {
        var session = await GetAsync(sessionId);
        if (session is null) return null;
        var ttl = TimeSpan.FromMinutes(session.IdleTimeoutMinutes);
        await Db.KeyExpireAsync(SessionKeys.Session(sessionId), ttl);
        await Db.KeyExpireAsync(SessionKeys.BookingDraft(sessionId), ttl);
        await Db.SortedSetAddAsync(SessionKeys.ExpiryIndex, sessionId, ExpiryScore(ttl));
        return session;
    }

    public async Task<bool> RemoveAsync(string sessionId)
    {
        var removed = await Db.KeyDeleteAsync(SessionKeys.Session(sessionId));
        await Db.KeyDeleteAsync(SessionKeys.BookingDraft(sessionId));
        await Db.SortedSetRemoveAsync(SessionKeys.ExpiryIndex, sessionId); // logout publishes its own SessionExpired
        return removed;
    }

    private static double ExpiryScore(TimeSpan ttl) => DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
}
