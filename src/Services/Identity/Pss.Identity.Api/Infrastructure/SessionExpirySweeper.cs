using MassTransit;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Events;
using StackExchange.Redis;

namespace Pss.Identity.Api.Infrastructure;

/// <summary>
/// Publishes SessionExpired for idle-timed-out sessions so Inventory releases holds and Booking drops drafts
/// (blueprint §5). ZREM is atomic, so with several Identity replicas only one publishes per session.
/// </summary>
public sealed class SessionExpirySweeper(IConnectionMultiplexer redis, IBus bus, ILogger<SessionExpirySweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Session sweep failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var due = await db.SortedSetRangeByScoreAsync(SessionKeys.ExpiryIndex, double.NegativeInfinity, now, take: 500);

        foreach (var member in due)
        {
            var sid = member.ToString();
            var ttl = await db.KeyTimeToLiveAsync(SessionKeys.Session(sid));
            if (ttl is not null)
            {
                // Still alive: fix the index score.
                await db.SortedSetAddAsync(SessionKeys.ExpiryIndex, member, DateTimeOffset.UtcNow.Add(ttl.Value).ToUnixTimeSeconds());
                continue;
            }

            if (await db.SortedSetRemoveAsync(SessionKeys.ExpiryIndex, member))
            {
                await bus.Publish(new SessionExpired(sid, "IdleTimeout", DateTime.UtcNow), ct);
                logger.LogInformation("Session {SessionId} expired", sid);
            }
        }
    }
}
