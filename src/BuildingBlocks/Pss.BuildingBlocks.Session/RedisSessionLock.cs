using Microsoft.Extensions.Configuration;
using Pss.BuildingBlocks.Domain;
using StackExchange.Redis;

namespace Pss.BuildingBlocks.Session;

public interface ISessionLock
{
    /// <summary>Serialises stateful calls per session (blueprint §5). Throws 409 core:Session:ConcurrentRequest after the wait.</summary>
    Task<IAsyncDisposable> AcquireAsync(string sessionId, CancellationToken ct = default);
}

public sealed class RedisSessionLock(IConnectionMultiplexer redis, IConfiguration configuration) : ISessionLock
{
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly TimeSpan _wait = TimeSpan.FromMilliseconds(configuration.GetValue("Session:LockWaitMs", 5000));
    private readonly TimeSpan _lease = TimeSpan.FromMilliseconds(configuration.GetValue("Session:LockLeaseMs", 30000));

    public async Task<IAsyncDisposable> AcquireAsync(string sessionId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = SessionKeys.Lock(sessionId);
        var token = Guid.NewGuid().ToString("N");
        var deadline = DateTime.UtcNow + _wait;

        while (true)
        {
            if (await db.StringSetAsync(key, token, _lease, When.NotExists))
                return new Releaser(db, key, token);

            if (DateTime.UtcNow >= deadline)
                throw DomainException.Conflict(ErrorCodes.SessionConcurrentRequest,
                    "Another stateful request is in progress for this session.");

            await Task.Delay(50, ct);
        }
    }

    private sealed class Releaser(IDatabase db, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
            => await db.ScriptEvaluateAsync(ReleaseScript, new RedisKey[] { key }, new RedisValue[] { token });
    }
}
