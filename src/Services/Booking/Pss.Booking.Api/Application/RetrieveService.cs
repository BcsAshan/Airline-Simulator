using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Keys;
using Pss.Booking.Api.Domain;
using Pss.Booking.Api.Infrastructure;
using Pss.Contracts.Booking;
using BookingModel = Pss.Contracts.Booking.Booking;

namespace Pss.Booking.Api.Application;

/// <summary>
/// Booking retrieve (blueprint §13.10). Strategy failures are indistinguishable (anti-enumeration):
/// not found / mismatch / strategy not allowed → the same 400 nsk:Booking:GeneralRestriction.
/// </summary>
public sealed class RetrieveService(BookingDbContext db, SellService sell, IConfiguration configuration)
{
    private static readonly string[] DefaultStrategies = { "lastName", "firstAndLastName", "email" };

    public async Task<BookingModel> RetrieveByStrategyAsync(DraftContext ctx, RetrieveQuery query, CancellationToken ct)
    {
        var rl = query.RecordLocator?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(rl) || rl.Length != 6) throw GeneralRestriction();

        var record = await db.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.RecordLocator == rl && b.Status != (int)BookingStatus.Default, ct);
        if (record is null) throw GeneralRestriction();

        var booking = Deserialize(record);
        var allowed = AllowedStrategies(ctx.Session.ApplicationName);
        if (!Matches(booking, query, allowed)) throw GeneralRestriction();

        return await LoadIntoStateAsync(ctx, record, booking, ct);
    }

    /// <summary>Agent: GET booking/retrieve/byRecordLocator/{rl} — loads into state.</summary>
    public async Task<BookingModel> RetrieveByRecordLocatorAsync(DraftContext ctx, string recordLocator, CancellationToken ct)
    {
        var record = await FindByRecordLocatorAsync(recordLocator, ct)
                     ?? throw new DomainException(ErrorCodes.InvalidKey, $"Booking {recordLocator} was not found.");
        return await LoadIntoStateAsync(ctx, record, Deserialize(record), ct);
    }

    /// <summary>Agent: GET booking/retrieve/{bookingKey} — loads into state.</summary>
    public async Task<BookingModel> RetrieveByBookingKeyAsync(DraftContext ctx, string bookingKey, CancellationToken ct)
    {
        var key = BookingKey.Parse(bookingKey);
        var record = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == key.BookingId && b.Status != 0, ct)
                     ?? throw DomainException.InvalidKey(bookingKey);
        return await LoadIntoStateAsync(ctx, record, Deserialize(record), ct);
    }

    /// <summary>Stateless (agent): GET /api/nsk/v1/bookings/{recordLocator}.</summary>
    public async Task<BookingModel?> GetStatelessAsync(string recordLocator, CancellationToken ct)
    {
        var record = await FindByRecordLocatorAsync(recordLocator, ct);
        return record is null ? null : Deserialize(record);
    }

    private Task<BookingRecord?> FindByRecordLocatorAsync(string recordLocator, CancellationToken ct)
    {
        var rl = recordLocator.Trim().ToUpperInvariant();
        return db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.RecordLocator == rl && b.Status != (int)BookingStatus.Default, ct);
    }

    /// <summary>Retrieving another booking overwrites the current one (uncommitted holds are released) — blueprint §5 rule 5.</summary>
    private async Task<BookingModel> LoadIntoStateAsync(DraftContext ctx, BookingRecord record, BookingModel booking, CancellationToken ct)
    {
        if (ctx.Draft is not null) await sell.TryReleaseAsync(ctx.Draft.UnconfirmedHoldIds(), ct);

        var paymentSeq = booking.Payments.Select(p => KeyCodec.TryDecode(p.PaymentKey, out _) ? PaymentKey.Parse(p.PaymentKey).Sequence : 0)
            .DefaultIfEmpty(0).Max();
        ctx.Set(new BookingDraft
        {
            Booking = booking,
            BookingId = record.BookingId,
            RowVersion = Convert.ToBase64String(record.RowVer),
            Committed = true,
            PaymentSequence = paymentSeq,
            CommentSequence = booking.Comments.Count,
            CreatedUtc = DateTime.UtcNow
        });
        return booking;
    }

    private string[] AllowedStrategies(string applicationName)
    {
        var configured = configuration.GetSection($"Booking:RetrieveStrategies:{applicationName}").Get<string[]>()
                         ?? configuration.GetSection("Booking:RetrieveStrategies:Default").Get<string[]>();
        return configured is { Length: > 0 } ? configured : DefaultStrategies;
    }

    private static bool Matches(BookingModel booking, RetrieveQuery q, string[] allowed)
    {
        bool Eq(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
                                         && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        bool Allowed(string s) => allowed.Contains(s, StringComparer.OrdinalIgnoreCase);

        var names = booking.Passengers.Values.Select(p => p.Name)
            .Concat(booking.Contacts.Values.Select(c => c.Name ?? new Name())).ToList();

        if (Allowed("firstAndLastName") && !string.IsNullOrWhiteSpace(q.FirstName) && !string.IsNullOrWhiteSpace(q.LastName)
            && names.Any(n => Eq(n.First, q.FirstName) && Eq(n.Last, q.LastName)))
            return true;

        if (Allowed("lastName") && string.IsNullOrWhiteSpace(q.FirstName) && names.Any(n => Eq(n.Last, q.LastName)))
            return true;

        if (Allowed("email") && booking.Contacts.Values.Any(c => Eq(c.EmailAddress, q.EmailAddress)))
            return true;

        return false;
    }

    private static BookingModel Deserialize(BookingRecord record)
        => JsonSerializer.Deserialize<BookingModel>(record.Document, PssJson.Options)
           ?? throw new DomainException(ErrorCodes.UnhandledException, "Stored booking could not be read.", 500);

    private static DomainException GeneralRestriction()
        => new(ErrorCodes.GeneralRestriction, "The booking could not be retrieved with the supplied information.");
}
