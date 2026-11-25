namespace Pss.Contracts.Internal;

// Service-to-service DTOs for /internal/inventory/* (never exposed through the gateway).

public class SegmentSearchQuery
{
    public string Origin { get; set; } = string.Empty;
    /// <summary>Null = any destination (used by Shopping to build connections).</summary>
    public string? Destination { get; set; }
    public DateTime BeginDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? CarrierCode { get; set; }
}

public class InvSegment
{
    public string CarrierCode { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string? OpSuffix { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateTime DepartureLocal { get; set; }
    public DateTime ArrivalLocal { get; set; }
    public DateTime DepartureUtc { get; set; }
    public DateTime ArrivalUtc { get; set; }
    public bool International { get; set; }
    public List<InvLeg> Legs { get; set; } = new();
    /// <summary>classOfService -> seats available (min across legs).</summary>
    public Dictionary<string, int> ClassAvailability { get; set; } = new();
    /// <summary>classOfService -> travel class / cabin code (e.g. Y, C).</summary>
    public Dictionary<string, string> ClassCabins { get; set; } = new();
}

public class InvLeg
{
    public long LegId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateTime DepartureLocal { get; set; }
    public DateTime ArrivalLocal { get; set; }
    public DateTime DepartureUtc { get; set; }
    public DateTime ArrivalUtc { get; set; }
    public int DepartureTimeVariant { get; set; }
    public int ArrivalTimeVariant { get; set; }
    public string EquipmentType { get; set; } = string.Empty;
    public string? EquipmentSuffix { get; set; }
    public int Capacity { get; set; }
    public int AdjustedCapacity { get; set; }
    public int Lid { get; set; }
    public int Sold { get; set; }
    public int Status { get; set; }
    public bool ETicket { get; set; }
    public string? PrbcCode { get; set; }
}

public class HoldItem
{
    public long LegId { get; set; }
    public string ClassOfService { get; set; } = string.Empty;
    public int Quantity { get; set; }
    /// <summary>Caller's grouping reference (Booking uses the journeyKey).</summary>
    public string? Reference { get; set; }
}

public class HoldRequest
{
    public List<HoldItem> Items { get; set; } = new();
}

public class HoldResult
{
    public long HoldId { get; set; }
    public long LegId { get; set; }
    public string ClassOfService { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Reference { get; set; }
    public DateTime ExpiresUtc { get; set; }
}

public class HoldResponse
{
    public List<HoldResult> Holds { get; set; } = new();
}

public class ConfirmHoldsRequest
{
    public List<long> HoldIds { get; set; } = new();
    public string RecordLocator { get; set; } = string.Empty;
}

public class ReleaseHoldsRequest
{
    public List<long> HoldIds { get; set; } = new();
}
