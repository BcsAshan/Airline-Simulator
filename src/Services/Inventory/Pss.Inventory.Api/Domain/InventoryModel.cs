namespace Pss.Inventory.Api.Domain;

// Inventory model (blueprint §15, §18): Flight → Leg → LegNest (cabin, LID) → LegClass (AU, sold).
// Sold counters exist at class, nest and leg level so every check is a single conditional row update.

public class Flight
{
    public long FlightId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string? OpSuffix { get; set; }
    /// <summary>Station-local departure date of the first leg.</summary>
    public DateTime DepartureDateLocal { get; set; }
    public List<Leg> Legs { get; set; } = new();
}

public class Leg
{
    public long LegId { get; set; }
    public long FlightId { get; set; }
    public Flight? Flight { get; set; }
    public int Seq { get; set; }
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
    public string? TailNumber { get; set; }
    public string? PrbcCode { get; set; }
    public int Capacity { get; set; }
    public int AdjustedCapacity { get; set; }
    /// <summary>Lid may exceed capacity (overbooking).</summary>
    public int Lid { get; set; }
    public int Sold { get; set; }
    /// <summary>LegStatus: 0 Normal, 1 Closed, 2 Canceled …</summary>
    public int Status { get; set; }
    public bool ETicket { get; set; }
    public bool International { get; set; }
    public byte[] RowVer { get; set; } = Array.Empty<byte>();
    public List<LegNest> Nests { get; set; } = new();
}

public class LegNest
{
    public long LegNestId { get; set; }
    public long LegId { get; set; }
    public Leg? Leg { get; set; }
    public string ClassNest { get; set; } = string.Empty;
    public string TravelClassCode { get; set; } = string.Empty;
    public int Lid { get; set; }
    public int AdjustedCapacity { get; set; }
    public int Sold { get; set; }
    public List<LegClass> Classes { get; set; } = new();
}

public class LegClass
{
    public long LegClassId { get; set; }
    public long LegNestId { get; set; }
    public LegNest? Nest { get; set; }
    public string ClassOfService { get; set; } = string.Empty;
    public int ClassRank { get; set; }
    public int AuthorizedUnits { get; set; }
    public int Sold { get; set; }
    public int CnxSold { get; set; }
    public int ThruSold { get; set; }
    /// <summary>1 = open for sale.</summary>
    public int Status { get; set; } = 1;
    public byte[] RowVer { get; set; } = Array.Empty<byte>();
}

public enum HoldStatus { Held = 0, Confirmed = 1, Released = 2, Expired = 3 }

public class InventoryHold
{
    public long HoldId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string? RecordLocator { get; set; }
    public long LegId { get; set; }
    public long LegNestId { get; set; }
    public long LegClassId { get; set; }
    public string ClassOfService { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public HoldStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Pure availability math (unit-tested).</summary>
public static class InventoryMath
{
    /// <summary>
    /// Available seats for a class = min(class AU − class sold, nest LID − nest sold, leg LID − leg sold), never negative.
    /// TODO(Phase 2): true nesting where higher-ranked classes can borrow lower-ranked inventory.
    /// </summary>
    public static int ClassAvailable(int classAu, int classSold, int nestLid, int nestSold, int legLid, int legSold)
        => Math.Max(0, Math.Min(classAu - classSold, Math.Min(nestLid - nestSold, legLid - legSold)));
}
