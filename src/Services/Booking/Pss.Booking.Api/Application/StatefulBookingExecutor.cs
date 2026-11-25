using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Session;
using Pss.Booking.Api.Domain;
using Pss.Booking.Api.Infrastructure;

namespace Pss.Booking.Api.Application;

/// <summary>
/// Stateful pipeline (blueprint §5): acquire session lock (≤5s else 409) → load draft → execute → save draft → release.
/// If the action throws, the draft is not saved (commands are all-or-nothing on the draft).
/// </summary>
public sealed class StatefulBookingExecutor(ISessionLock locks, DraftStore drafts, ServerTimingCollector timing)
{
    public async Task<T> ExecuteAsync<T>(SessionInfo session, Func<DraftContext, Task<T>> action, CancellationToken ct)
    {
        await using var sessionLock = await locks.AcquireAsync(session.SessionId, ct);
        var draft = await timing.Measure("draft-load", () => drafts.GetAsync(session.SessionId));
        var context = new DraftContext(session, draft);

        var result = await action(context);

        if (context.Cleared) await drafts.DeleteAsync(session.SessionId);
        else if (context.Changed && context.Draft is not null) await timing.Measure("draft-save", async () =>
        {
            await drafts.SaveAsync(session, context.Draft);
            return true;
        });
        return result;
    }
}

public sealed class DraftContext(SessionInfo session, BookingDraft? draft)
{
    public SessionInfo Session { get; } = session;
    public BookingDraft? Draft { get; private set; } = draft;
    public bool Changed { get; private set; }
    public bool Cleared { get; private set; }

    public BookingDraft Require()
        => Draft ?? throw new DomainException(ErrorCodes.NoBookingInState, "There is no booking in state for this session.");

    public void Set(BookingDraft draft)
    {
        Draft = draft;
        Changed = true;
        Cleared = false;
    }

    public void MarkChanged() => Changed = true;

    public void Clear()
    {
        Draft = null;
        Cleared = true;
    }
}
