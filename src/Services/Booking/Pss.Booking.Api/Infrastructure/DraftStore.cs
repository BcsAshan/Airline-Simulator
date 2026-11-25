using System.Text.Json;
using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Session;
using Pss.Booking.Api.Domain;
using StackExchange.Redis;

namespace Pss.Booking.Api.Infrastructure;

/// <summary>Redis draft store: pss:session:{sid}:booking, TTL = session idle timeout (slid by every request).</summary>
public sealed class DraftStore(IConnectionMultiplexer redis)
{
    public async Task<BookingDraft?> GetAsync(string sessionId)
    {
        var value = await redis.GetDatabase().StringGetAsync(SessionKeys.BookingDraft(sessionId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<BookingDraft>(value.ToString(), PssJson.Options);
    }

    public Task SaveAsync(SessionInfo session, BookingDraft draft)
        => redis.GetDatabase().StringSetAsync(SessionKeys.BookingDraft(session.SessionId),
            JsonSerializer.Serialize(draft, PssJson.Options), TimeSpan.FromMinutes(Math.Max(1, session.IdleTimeoutMinutes)));

    public Task DeleteAsync(string sessionId) => redis.GetDatabase().KeyDeleteAsync(SessionKeys.BookingDraft(sessionId));
}
