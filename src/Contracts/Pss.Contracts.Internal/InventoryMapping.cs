using System.Globalization;
using Pss.BuildingBlocks.Keys;
using Pss.Contracts.Booking;

namespace Pss.Contracts.Internal;

/// <summary>Maps inventory snapshots to the public contract (keys, legs, designators). Shared by Shopping and Booking.</summary>
public static class InventoryMapping
{
    public static SegmentKey ToSegmentKey(this InvSegment s)
        => new(s.CarrierCode, s.FlightNumber, s.OpSuffix, s.Origin, s.DepartureLocal, s.Destination, s.ArrivalLocal);

    public static string JourneyKeyFor(IEnumerable<InvSegment> segments)
        => JourneyKey.Encode(segments.Select(ToSegmentKey));

    /// <summary>"yyyyMMdd CCnnnn ORGDST" (e.g. "20230901 NV2555 SLCJFK").</summary>
    public static string FlightReference(string carrier, string flightNumber, DateTime departureLocal, string origin, string destination)
        => $"{departureLocal.ToString("yyyyMMdd", CultureInfo.InvariantCulture)} {carrier}{flightNumber.Trim().PadLeft(4)} {origin}{destination}";

    public static Designator ToDesignator(string origin, string destination, DateTime departureLocal, DateTime arrivalLocal)
        => new() { Origin = origin, Destination = destination, Departure = departureLocal, Arrival = arrivalLocal };

    public static FlightType FlightTypeFor(IReadOnlyList<InvSegment> segments)
        => segments.Count > 1 ? FlightType.Connect : segments[0].Legs.Count > 1 ? FlightType.Through : FlightType.NonStop;

    public static int StopsFor(IReadOnlyList<InvSegment> segments) => segments.Sum(s => s.Legs.Count) - 1;

    public static Leg ToLeg(InvSegment segment, InvLeg leg) => new()
    {
        LegKey = new LegKey(leg.DepartureLocal, segment.CarrierCode, segment.FlightNumber, segment.OpSuffix, leg.Origin, leg.Destination, leg.LegId).Encode(),
        Designator = ToDesignator(leg.Origin, leg.Destination, leg.DepartureLocal, leg.ArrivalLocal),
        SeatmapReference = SeatmapReference.Encode(segment.CarrierCode, segment.FlightNumber, segment.OpSuffix, leg.DepartureLocal, leg.Origin, leg.Destination),
        FlightReference = FlightReference(segment.CarrierCode, segment.FlightNumber, leg.DepartureLocal, leg.Origin, leg.Destination),
        LegInfo = new LegInfo
        {
            DepartureTimeUtc = DateTime.SpecifyKind(leg.DepartureUtc, DateTimeKind.Utc),
            ArrivalTimeUtc = DateTime.SpecifyKind(leg.ArrivalUtc, DateTimeKind.Utc),
            DepartureTime = DateTime.SpecifyKind(leg.DepartureLocal, DateTimeKind.Unspecified),
            ArrivalTime = DateTime.SpecifyKind(leg.ArrivalLocal, DateTimeKind.Unspecified),
            DepartureTimeVariant = leg.DepartureTimeVariant,
            ArrivalTimeVariant = leg.ArrivalTimeVariant,
            AdjustedCapacity = leg.AdjustedCapacity,
            Capacity = leg.Capacity,
            Lid = leg.Lid,
            Sold = leg.Sold,
            Status = (LegStatus)leg.Status,
            EquipmentType = leg.EquipmentType,
            EquipmentTypeSuffix = leg.EquipmentSuffix,
            ETicket = leg.ETicket,
            PrbcCode = leg.PrbcCode
        }
    };
}
