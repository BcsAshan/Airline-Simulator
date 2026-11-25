namespace Pss.Contracts.Booking;

public class BookingBreakdown
{
    public decimal BalanceDue { get; set; }
    public decimal PointsBalanceDue { get; set; }
    public decimal AuthorizedBalanceDue { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalPoints { get; set; }
    public decimal TotalToCollect { get; set; }
    public decimal TotalPointsToCollect { get; set; }
    public decimal TotalCharged { get; set; }
    public PassengerTotals PassengerTotals { get; set; } = new();
    public Dictionary<string, PassengerBreakdown> Passengers { get; set; } = new();
    public JourneyTotals JourneyTotals { get; set; } = new();
    public Dictionary<string, JourneyBreakdown> Journeys { get; set; } = new();
    public AddOnTotals AddOnTotals { get; set; } = new();
}

public class PassengerTotals
{
    public ChargeTotal? Services { get; set; }
    public ChargeTotal? SpecialServices { get; set; }
    public ChargeTotal? Seats { get; set; }
    public ChargeTotal? Upgrades { get; set; }
    public ChargeTotal? Spoilage { get; set; }
    public ChargeTotal? NameChanges { get; set; }
    public ChargeTotal? Convenience { get; set; }
    public ChargeTotal? Infant { get; set; }
}

public class PassengerBreakdown : PassengerTotals
{
    public string PassengerKey { get; set; } = string.Empty;
}

public class ChargeTotal
{
    public decimal Total { get; set; }
    public decimal Taxes { get; set; }
    public decimal Adjustments { get; set; }
    public List<ServiceCharge> Charges { get; set; } = new();
}

public class JourneyTotals
{
    public decimal TotalAmount { get; set; }
    public decimal TotalPoints { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalDiscount { get; set; }
}

public class JourneyBreakdown : JourneyTotals
{
    public string JourneyKey { get; set; } = string.Empty;
}

public class AddOnTotals
{
    public decimal? Car { get; set; }
    public decimal? Hotel { get; set; }
    public decimal? Activities { get; set; }
}

public class ServiceCharge
{
    public decimal Amount { get; set; }
    public string? Code { get; set; }
    public string? Detail { get; set; }
    public ChargeType Type { get; set; }
    public int CollectType { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string? ForeignCurrencyCode { get; set; }
    public decimal ForeignAmount { get; set; }
    public string? TicketCode { get; set; }
}
