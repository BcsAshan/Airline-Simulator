using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Domain;
using Pss.Contracts.Internal;
using Pss.Inventory.Api.Domain;
using Pss.Inventory.Api.Infrastructure;

namespace Pss.Inventory.Api.Application;

/// <summary>
/// Single writer of seat inventory (blueprint §7.3, §15). Holds increment sold immediately (other sessions see it)
/// using conditional row updates inside one transaction, ordered by LegId to avoid deadlocks.
/// </summary>
public sealed class InventoryService(InventoryDbContext db, IConfiguration configuration, TimeProvider clock, ILogger<InventoryService> logger)
{
    private TimeSpan HoldTtl => TimeSpan.FromMinutes(configuration.GetValue("Inventory:HoldSafetyTtlMinutes", 60));

    // ------------------------------------------------------------------ search

    public async Task<List<InvSegment>> SearchSegmentsAsync(SegmentSearchQuery query, CancellationToken ct)
    {
        var begin = query.BeginDate.Date;
        var endExclusive = (query.EndDate == default ? query.BeginDate : query.EndDate).Date.AddDays(1);
        if (endExclusive <= begin || (endExclusive - begin).TotalDays > 31)
            throw new DomainException("nsk-server:InvalidDateRange", "The date range must be between 1 and 31 days.");

        var origin = query.Origin.Trim().ToUpperInvariant();
        var destination = string.IsNullOrWhiteSpace(query.Destination) ? null : query.Destination.Trim().ToUpperInvariant();
        var now = clock.GetUtcNow().UtcDateTime;

        var flightIds = await db.Legs.AsNoTracking()
            .Where(l => l.Origin == origin && l.DepartureLocal >= begin && l.DepartureLocal < endExclusive && l.Status == 0)
            .Select(l => l.FlightId).Distinct().ToListAsync(ct);
        if (flightIds.Count == 0) return new List<InvSegment>();

        var flights = await db.Flights.AsNoTracking()
            .Where(f => flightIds.Contains(f.FlightId))
            .Include(f => f.Legs).ThenInclude(l => l.Nests).ThenInclude(n => n.Classes)
            .AsSplitQuery()
            .ToListAsync(ct);

        var result = new List<InvSegment>();
        foreach (var flight in flights)
        {
            if (query.CarrierCode is not null && flight.CarrierCode != query.CarrierCode) continue;
            var legs = flight.Legs.OrderBy(l => l.Seq).ToList();
            for (var i = 0; i < legs.Count; i++)
            {
                var first = legs[i];
                if (first.Origin != origin || first.DepartureLocal < begin || first.DepartureLocal >= endExclusive) continue;
                if (first.DepartureUtc <= now) continue; // already departed

                for (var j = i; j < legs.Count; j++)
                {
                    if (legs[j].Status != 0) break;
                    if (destination is not null && legs[j].Destination != destination) continue;
                    result.Add(BuildSegment(flight, legs.GetRange(i, j - i + 1)));
                }
            }
        }

        return result.OrderBy(s => s.DepartureUtc).ToList();
    }

    private static InvSegment BuildSegment(Flight flight, List<Leg> legs)
    {
        var first = legs[0];
        var last = legs[^1];
        var segment = new InvSegment
        {
            CarrierCode = flight.CarrierCode,
            FlightNumber = flight.FlightNumber,
            OpSuffix = flight.OpSuffix,
            Origin = first.Origin,
            Destination = last.Destination,
            DepartureLocal = first.DepartureLocal,
            ArrivalLocal = last.ArrivalLocal,
            DepartureUtc = DateTime.SpecifyKind(first.DepartureUtc, DateTimeKind.Utc),
            ArrivalUtc = DateTime.SpecifyKind(last.ArrivalUtc, DateTimeKind.Utc),
            International = legs.Any(l => l.International),
            Legs = legs.Select(l => new InvLeg
            {
                LegId = l.LegId,
                Origin = l.Origin,
                Destination = l.Destination,
                DepartureLocal = l.DepartureLocal,
                ArrivalLocal = l.ArrivalLocal,
                DepartureUtc = DateTime.SpecifyKind(l.DepartureUtc, DateTimeKind.Utc),
                ArrivalUtc = DateTime.SpecifyKind(l.ArrivalUtc, DateTimeKind.Utc),
                DepartureTimeVariant = l.DepartureTimeVariant,
                ArrivalTimeVariant = l.ArrivalTimeVariant,
                EquipmentType = l.EquipmentType,
                EquipmentSuffix = l.EquipmentSuffix,
                Capacity = l.Capacity,
                AdjustedCapacity = l.AdjustedCapacity,
                Lid = l.Lid,
                Sold = l.Sold,
                Status = l.Status,
                ETicket = l.ETicket,
                PrbcCode = l.PrbcCode
            }).ToList()
        };

        // Segment availability per class = min over legs (class must exist on every leg).
        Dictionary<string, int>? availability = null;
        foreach (var leg in legs)
        {
            var legAvail = new Dictionary<string, int>();
            foreach (var nest in leg.Nests)
            foreach (var cls in nest.Classes.Where(c => c.Status == 1))
            {
                legAvail[cls.ClassOfService] = InventoryMath.ClassAvailable(cls.AuthorizedUnits, cls.Sold, nest.Lid, nest.Sold, leg.Lid, leg.Sold);
                segment.ClassCabins[cls.ClassOfService] = nest.TravelClassCode;
            }

            availability = availability is null
                ? legAvail
                : availability.Where(kv => legAvail.ContainsKey(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => Math.Min(kv.Value, legAvail[kv.Key]));
        }

        segment.ClassAvailability = availability ?? new Dictionary<string, int>();
        return segment;
    }

    // ------------------------------------------------------------------ holds

    public Task<HoldResponse> HoldAsync(string sessionId, HoldRequest request, CancellationToken ct)
        => InTransactionAsync(async () =>
        {
            if (request.Items.Count == 0) return new HoldResponse();
            if (request.Items.Any(i => i.Quantity is <= 0 or > 99))
                throw new DomainException(ErrorCodes.MaxBookingPassengers, "Hold quantity must be between 1 and 99.");

            var legIds = request.Items.Select(i => i.LegId).Distinct().ToList();
            var classes = await db.LegClasses.AsNoTracking()
                .Where(c => legIds.Contains(c.Nest!.LegId))
                .Select(c => new { c.LegClassId, c.LegNestId, c.Nest!.LegId, c.ClassOfService })
                .ToListAsync(ct);

            var resolved = request.Items
                .Select(item =>
                {
                    var cls = classes.FirstOrDefault(c => c.LegId == item.LegId && c.ClassOfService == item.ClassOfService)
                              ?? throw NotAvailable(item.LegId, item.ClassOfService);
                    return (Item: item, cls.LegClassId, cls.LegNestId);
                })
                .OrderBy(x => x.Item.LegId).ThenBy(x => x.LegClassId)
                .ToList();

            var expires = clock.GetUtcNow().UtcDateTime.Add(HoldTtl);
            var holds = new List<InventoryHold>();

            foreach (var (item, legClassId, legNestId) in resolved)
            {
                var qty = item.Quantity;
                var legRows = await db.Legs
                    .Where(l => l.LegId == item.LegId && l.Status == 0 && l.Lid - l.Sold >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.Sold, l => l.Sold + qty), ct);
                var nestRows = legRows == 0 ? 0 : await db.LegNests
                    .Where(n => n.LegNestId == legNestId && n.Lid - n.Sold >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(n => n.Sold, n => n.Sold + qty), ct);
                var classRows = nestRows == 0 ? 0 : await db.LegClasses
                    .Where(c => c.LegClassId == legClassId && c.Status == 1 && c.AuthorizedUnits - c.Sold >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Sold, c => c.Sold + qty), ct);

                if (classRows == 0) throw NotAvailable(item.LegId, item.ClassOfService); // rollback (all-or-nothing)

                holds.Add(new InventoryHold
                {
                    SessionId = sessionId,
                    LegId = item.LegId,
                    LegNestId = legNestId,
                    LegClassId = legClassId,
                    ClassOfService = item.ClassOfService,
                    Quantity = qty,
                    Status = HoldStatus.Held,
                    CreatedUtc = clock.GetUtcNow().UtcDateTime,
                    ExpiresUtc = expires,
                    Reference = item.Reference
                });
            }

            db.Holds.AddRange(holds);
            await db.SaveChangesAsync(ct);

            return new HoldResponse
            {
                Holds = holds.Select(h => new HoldResult
                {
                    HoldId = h.HoldId, LegId = h.LegId, ClassOfService = h.ClassOfService, Quantity = h.Quantity,
                    Reference = h.Reference, ExpiresUtc = DateTime.SpecifyKind(h.ExpiresUtc, DateTimeKind.Utc)
                }).ToList()
            };
        }, ct);

    /// <summary>Commit: Held → Confirmed (idempotent for holds already confirmed under the same record locator).</summary>
    public Task<int> ConfirmAsync(string sessionId, ConfirmHoldsRequest request, CancellationToken ct)
        => InTransactionAsync(async () =>
        {
            var ids = request.HoldIds.Distinct().ToList();
            if (ids.Count == 0) return 0;

            var confirmed = await db.Holds
                .Where(h => ids.Contains(h.HoldId) && h.Status == HoldStatus.Held && h.SessionId == sessionId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(h => h.Status, HoldStatus.Confirmed)
                    .SetProperty(h => h.RecordLocator, request.RecordLocator), ct);

            var alreadyConfirmed = await db.Holds.CountAsync(h => ids.Contains(h.HoldId)
                && h.Status == HoldStatus.Confirmed && h.RecordLocator == request.RecordLocator, ct);

            if (alreadyConfirmed != ids.Count)
                throw new DomainException(ErrorCodes.ClassNotAvailable,
                    "Some held seats have expired; please search and sell again.", 400,
                    new { requested = ids.Count, confirmed = alreadyConfirmed });

            return confirmed;
        }, ct);

    public Task<int> ReleaseAsync(string? sessionId, IReadOnlyCollection<long> holdIds, HoldStatus releaseAs, CancellationToken ct)
        => InTransactionAsync(async () =>
        {
            var released = 0;
            foreach (var id in holdIds.Distinct().OrderBy(x => x))
            {
                var hold = await db.Holds.AsNoTracking().FirstOrDefaultAsync(h => h.HoldId == id, ct);
                if (hold is null || hold.Status != HoldStatus.Held) continue;
                if (sessionId is not null && hold.SessionId != sessionId) continue;

                var rows = await db.Holds.Where(h => h.HoldId == id && h.Status == HoldStatus.Held)
                    .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, releaseAs), ct);
                if (rows == 0) continue;

                var qty = hold.Quantity;
                await db.LegClasses.Where(c => c.LegClassId == hold.LegClassId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Sold, c => c.Sold - qty), ct);
                await db.LegNests.Where(n => n.LegNestId == hold.LegNestId)
                    .ExecuteUpdateAsync(s => s.SetProperty(n => n.Sold, n => n.Sold - qty), ct);
                await db.Legs.Where(l => l.LegId == hold.LegId)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.Sold, l => l.Sold - qty), ct);
                released++;
            }
            return released;
        }, ct);

    public async Task<int> ReleaseSessionAsync(string sessionId, CancellationToken ct)
    {
        var ids = await db.Holds.AsNoTracking()
            .Where(h => h.SessionId == sessionId && h.Status == HoldStatus.Held)
            .Select(h => h.HoldId).ToListAsync(ct);
        if (ids.Count == 0) return 0;
        var n = await ReleaseAsync(sessionId, ids, HoldStatus.Released, ct);
        logger.LogInformation("Released {Count} holds for expired session {SessionId}", n, sessionId);
        return n;
    }

    /// <summary>Safety net (blueprint §5): expire holds whose session never reported expiry.</summary>
    public async Task<int> SweepExpiredAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await db.Holds.AsNoTracking()
            .Where(h => h.Status == HoldStatus.Held && h.ExpiresUtc < now)
            .OrderBy(h => h.HoldId).Take(200)
            .Select(h => h.HoldId).ToListAsync(ct);
        return ids.Count == 0 ? 0 : await ReleaseAsync(null, ids, HoldStatus.Expired, ct);
    }

    private static DomainException NotAvailable(long legId, string classOfService)
        => new(ErrorCodes.ClassNotAvailable, $"Class {classOfService} is not available on the requested flight.", 400,
            new { legId, classOfService });

    /// <summary>Runs inside a transaction compatible with the SQL retry execution strategy (or the ambient one).</summary>
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return await work();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });
    }
}
