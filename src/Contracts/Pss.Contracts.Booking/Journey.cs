namespace Pss.Contracts.Booking;

public class Designator
{
    public string Destination { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public DateTime Arrival { get; set; }
    public DateTime Departure { get; set; }
}

public class Journey
{
    public FlightType FlightType { get; set; }
    public int Stops { get; set; }
    public Designator Designator { get; set; } = new();
    public JourneyMove Move { get; set; } = new();
    public List<Segment> Segments { get; set; } = new();
    public string JourneyKey { get; set; } = string.Empty;
    public bool NotForGeneralUser { get; set; }
}

public class JourneyMove
{
    public int MaxMoveBackDays { get; set; } = 365;
    public int MaxMoveOutDays { get; set; } = 365;
}

public class FlightIdentifier
{
    public string Identifier { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string? OpSuffix { get; set; }
}

public class Segment
{
    public bool IsStandby { get; set; }
    public bool IsConfirming { get; set; }
    public bool IsConfirmingExternal { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsHosted { get; set; } = true;
    public bool IsChangeOfGauge { get; set; }
    public Designator Designator { get; set; } = new();
    public bool IsSeatmapViewable { get; set; } = true;
    public List<SegmentFare> Fares { get; set; } = new();
    public string SegmentKey { get; set; } = string.Empty;
    public FlightIdentifier Identifier { get; set; } = new();
    public Dictionary<string, PassengerSegment> PassengerSegment { get; set; } = new();
    public ChannelType ChannelType { get; set; }
    public string? CabinOfService { get; set; }
    public FlightIdentifier? ExternalIdentifier { get; set; }
    public string? PriorityCode { get; set; }
    public int ChangeReasonCode { get; set; }
    public int SegmentType { get; set; }
    public DateTime? SalesDate { get; set; }
    public bool International { get; set; }
    public string? FlightReference { get; set; }
    public SegmentStatus Status { get; set; }
    public List<Leg> Legs { get; set; } = new();
}

public class SegmentFare
{
    public bool IsGoverning { get; set; } = true;
    public string CarrierCode { get; set; } = string.Empty;
    public string FareKey { get; set; } = string.Empty;
    public string ClassOfService { get; set; } = string.Empty;
    public string? ClassType { get; set; }
    public int FareApplicationType { get; set; }
    public string? FareClassOfService { get; set; }
    public string? FareBasisCode { get; set; }
    public int FareSequence { get; set; }
    public int InboundOutBound { get; set; }
    public int FareStatus { get; set; }
    public bool IsAllotmentMarketFare { get; set; }
    public string? OriginalClassOfService { get; set; }
    public string? RuleNumber { get; set; }
    public string? ProductClass { get; set; }
    public string? RuleTariff { get; set; }
    public string? TravelClassCode { get; set; }
    public DateTime? PricingDate { get; set; }
    public List<PassengerFare> PassengerFares { get; set; } = new();
    public int FareLink { get; set; }
}

public class PassengerFare
{
    public List<ServiceCharge> ServiceCharges { get; set; } = new();
    public string? DiscountCode { get; set; }
    public string? FareDiscountCode { get; set; }
    public string PassengerType { get; set; } = "ADT";
}

public class PassengerSegment
{
    public List<object> Seats { get; set; } = new();
    public string PassengerKey { get; set; } = string.Empty;
    public DateTime? ActivityDate { get; set; }
    public string? BoardingSequence { get; set; }
    public DateTime? CreatedDate { get; set; }
    public LiftStatus LiftStatus { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public int OverBookIndicator { get; set; }
    public DateTime? PriorityDate { get; set; }
    public bool TimeChanged { get; set; }
    public List<object> Ssrs { get; set; } = new();
    public List<object> Tickets { get; set; } = new();
    public List<object> Bags { get; set; } = new();
    public bool HasInfant { get; set; }
    public string? BundleCode { get; set; }
    public int? BaggageGroupNumber { get; set; }
}

public class Leg
{
    public string LegKey { get; set; } = string.Empty;
    public object? OperationsInfo { get; set; }
    public Designator Designator { get; set; } = new();
    public LegInfo LegInfo { get; set; } = new();
    public List<object> Nests { get; set; } = new();
    public List<object> Ssrs { get; set; } = new();
    public string? SeatmapReference { get; set; }
    public string? FlightReference { get; set; }
}

public class LegInfo
{
    public DateTime DepartureTimeUtc { get; set; }
    public DateTime ArrivalTimeUtc { get; set; }
    public int AdjustedCapacity { get; set; }
    public string? ArrivalTerminal { get; set; }
    public int ArrivalTimeVariant { get; set; }
    public int Capacity { get; set; }
    public string? DepartureTerminal { get; set; }
    public int DepartureTimeVariant { get; set; }
    public string? EquipmentType { get; set; }
    public string? EquipmentTypeSuffix { get; set; }
    public bool ETicket { get; set; }
    public bool Irop { get; set; }
    public int Lid { get; set; }
    public string? OperatingCarrier { get; set; }
    public DateTime ArrivalTime { get; set; }
    public DateTime DepartureTime { get; set; }
    public string? PrbcCode { get; set; }
    public int Sold { get; set; }
    public LegStatus Status { get; set; }
}
