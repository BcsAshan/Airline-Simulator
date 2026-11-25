namespace Pss.Contracts.Internal;

/// <summary>What Shopping remembers about one fareAvailabilityKey (Redis offer store, blueprint §7.5).</summary>
public class OfferJourneyFare
{
    public string FareAvailabilityKey { get; set; } = string.Empty;
    public string JourneyKey { get; set; } = string.Empty;
    public int FlightType { get; set; }
    public int Stops { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string? PromotionCode { get; set; }
    public List<OfferSegment> Segments { get; set; } = new();
}

public class OfferSegment
{
    public string SegmentKey { get; set; } = string.Empty;
    public InvSegment Segment { get; set; } = new();
    public long FareId { get; set; }
    public string FareKey { get; set; } = string.Empty;
    public string ClassOfService { get; set; } = string.Empty;
    public string? ProductClass { get; set; }
    public string? TravelClassCode { get; set; }
}
