using System.Globalization;
using Pss.BuildingBlocks.Domain;

namespace Pss.BuildingBlocks.Keys;

/// <summary>passengerKey = "passengerNumber!PTC" (e.g. "0!ADT"). Note: the key changes when the PTC changes.</summary>
public readonly record struct PassengerKey(int Number, string PassengerTypeCode)
{
    public string Encode() => KeyCodec.Encode('!', Number, PassengerTypeCode);
    public override string ToString() => Encode();

    public static PassengerKey Parse(string? key)
    {
        var p = KeyCodec.DecodeParts(key, '!', 2);
        if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) throw DomainException.InvalidKey(key);
        return new PassengerKey(n, p[1]);
    }
}

/// <summary>
/// Segment designator key (also used for single-segment journeyKey):
/// "CC~nnnn~S~~ORG~MM/dd/yyyy HH:mm~DST~MM/dd/yyyy HH:mm~~". Connections join segments with '^'.
/// </summary>
public sealed record SegmentKey(string CarrierCode, string FlightNumber, string? OpSuffix,
    string Origin, DateTime DepartureLocal, string Destination, DateTime ArrivalLocal)
{
    private const string DateFormat = "MM'/'dd'/'yyyy HH':'mm";

    public string ToRaw() => string.Join('~',
        CarrierCode, FlightNumber.Trim().PadLeft(4), string.IsNullOrEmpty(OpSuffix) ? " " : OpSuffix, string.Empty,
        Origin, DepartureLocal.ToString(DateFormat, CultureInfo.InvariantCulture),
        Destination, ArrivalLocal.ToString(DateFormat, CultureInfo.InvariantCulture), string.Empty, string.Empty);

    public string Encode() => KeyCodec.Encode(ToRaw());

    public static SegmentKey FromRaw(string raw, string originalKey)
    {
        var p = raw.Split('~');
        if (p.Length < 8) throw DomainException.InvalidKey(originalKey);
        if (!DateTime.TryParseExact(p[5], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dep) ||
            !DateTime.TryParseExact(p[7], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var arr))
            throw DomainException.InvalidKey(originalKey);
        return new SegmentKey(p[0], p[1].Trim(), string.IsNullOrWhiteSpace(p[2]) ? null : p[2], p[4], dep, p[6], arr);
    }

    public static SegmentKey Parse(string? key) => FromRaw(KeyCodec.Decode(key), key ?? string.Empty);
}

/// <summary>journeyKey = segment raw keys joined by '^'.</summary>
public static class JourneyKey
{
    public static string Encode(IEnumerable<SegmentKey> segments) => KeyCodec.Encode(string.Join('^', segments.Select(s => s.ToRaw())));

    public static IReadOnlyList<SegmentKey> Parse(string? key)
    {
        var raw = KeyCodec.Decode(key);
        return raw.Split('^').Select(r => SegmentKey.FromRaw(r, key ?? string.Empty)).ToList();
    }
}

/// <summary>legKey = "&lt;local departure ticks&gt;!CC!nnnn!S!ORG!DST!&lt;inventoryLegId&gt;".</summary>
public readonly record struct LegKey(DateTime DepartureLocal, string CarrierCode, string FlightNumber, string? OpSuffix,
    string Origin, string Destination, long LegId)
{
    public string Encode() => KeyCodec.Encode('!', DepartureLocal.Ticks, CarrierCode, FlightNumber.Trim(),
        string.IsNullOrEmpty(OpSuffix) ? " " : OpSuffix, Origin, Destination, LegId);

    public static LegKey Parse(string? key)
    {
        var p = KeyCodec.DecodeParts(key, '!', 7);
        if (!long.TryParse(p[0], out var ticks) || !long.TryParse(p[6], out var legId)) throw DomainException.InvalidKey(key);
        return new LegKey(new DateTime(ticks), p[1], p[2], string.IsNullOrWhiteSpace(p[3]) ? null : p[3], p[4], p[5], legId);
    }
}

/// <summary>seatmapReference = "CC!nnnn!S!&lt;ticks&gt;!ORG!DST".</summary>
public static class SeatmapReference
{
    public static string Encode(string carrier, string flightNumber, string? opSuffix, DateTime departureLocal, string origin, string destination)
        => KeyCodec.Encode('!', carrier, flightNumber.Trim(), string.IsNullOrEmpty(opSuffix) ? " " : opSuffix, departureLocal.Ticks, origin, destination);
}

/// <summary>fareAvailabilityKey = "offerId!index" (offer cached by Shopping).</summary>
public readonly record struct FareAvailabilityKey(string OfferId, int Index)
{
    public string Encode() => KeyCodec.Encode('!', OfferId, Index);

    public static FareAvailabilityKey Parse(string? key)
    {
        var p = KeyCodec.DecodeParts(key, '!', 2);
        if (!int.TryParse(p[^1], out var idx) || string.IsNullOrWhiteSpace(p[0])) throw DomainException.InvalidKey(key);
        return new FareAvailabilityKey(p[0], idx);
    }
}

/// <summary>fareKey = "fareId~class~carrier~fareBasis~rule~sequence".</summary>
public readonly record struct FareKey(long FareId, string ClassOfService, string CarrierCode, string FareBasisCode, string RuleNumber, int FareSequence)
{
    public string Encode() => KeyCodec.Encode('~', FareId, ClassOfService, CarrierCode, FareBasisCode, RuleNumber, FareSequence);

    public static FareKey Parse(string? key)
    {
        var p = KeyCodec.DecodeParts(key, '~', 6);
        if (!long.TryParse(p[0], out var id) || !int.TryParse(p[5], out var seq)) throw DomainException.InvalidKey(key);
        return new FareKey(id, p[1], p[2], p[3], p[4], seq);
    }
}

/// <summary>bookingKey = "bookingId!recordLocator!archived" (e.g. "126092!C1KNTY!false").</summary>
public readonly record struct BookingKey(long BookingId, string RecordLocator, bool Archived)
{
    public string Encode() => KeyCodec.Encode('!', BookingId, RecordLocator, Archived);

    public static BookingKey Parse(string? key)
    {
        var p = KeyCodec.DecodeParts(key, '!', 3);
        if (!long.TryParse(p[0], out var id)) throw DomainException.InvalidKey(key);
        return new BookingKey(id, p[1], string.Equals(p[2], "true", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>paymentKey = "paymentId!sequence!methodCode!bookingId" (pre-commit: "0!1!CA!0").</summary>
public readonly record struct PaymentKey(long PaymentId, int Sequence, string MethodCode, long BookingId)
{
    public string Encode() => KeyCodec.Encode('!', PaymentId, Sequence, MethodCode, BookingId);

    public static PaymentKey Parse(string? key)
    {
        var p = KeyCodec.DecodeParts(key, '!', 4);
        if (!long.TryParse(p[0], out var pid) || !int.TryParse(p[1], out var seq) || !long.TryParse(p[3], out var bid))
            throw DomainException.InvalidKey(key);
        return new PaymentKey(pid, seq, p[2], bid);
    }
}
