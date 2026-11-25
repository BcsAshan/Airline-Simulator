using BookingModel = Pss.Contracts.Booking.Booking;

namespace Pss.Booking.Api.Domain;

/// <summary>
/// "Booking in state" (blueprint §5): the full public Booking aggregate plus internal bookkeeping,
/// stored in Redis under pss:session:{sid}:booking. Nothing is persisted until commit.
/// </summary>
public sealed class BookingDraft
{
    public BookingModel Booking { get; set; } = new();

    /// <summary>SQL id once a commit has created the record.</summary>
    public long? BookingId { get; set; }

    /// <summary>Base64 SQL rowversion of the stored booking (optimistic concurrency for PUT commit).</summary>
    public string? RowVersion { get; set; }

    /// <summary>True once the booking has been committed at least once.</summary>
    public bool Committed { get; set; }

    public List<DraftHold> Holds { get; set; } = new();
    public int PaymentSequence { get; set; }

    /// <summary>A seat was assigned or removed since the last commit, so commit must sync seats with the Seat service.</summary>
    public bool SeatsChanged { get; set; }
    public int CommentSequence { get; set; }
    public DateTime CreatedUtc { get; set; }

    public List<long> UnconfirmedHoldIds() => Holds.Where(h => !h.Confirmed).Select(h => h.HoldId).ToList();
}

public sealed class DraftHold
{
    public long HoldId { get; set; }
    public string JourneyKey { get; set; } = string.Empty;
    public bool Confirmed { get; set; }
}
