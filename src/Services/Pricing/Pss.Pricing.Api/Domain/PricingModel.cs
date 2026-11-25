namespace Pss.Pricing.Api.Domain;

/// <summary>Filed fare (blueprint §7.4). One-way, per market + class; reverse market filed separately.</summary>
public class Fare
{
    public long FareId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string ClassOfService { get; set; } = string.Empty;
    public string FareBasisCode { get; set; } = string.Empty;
    public string RuleNumber { get; set; } = string.Empty;
    public string? ProductClass { get; set; }
    public string? TravelClassCode { get; set; }
    public int FareSequence { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateTime? TravelFrom { get; set; }
    public DateTime? TravelTo { get; set; }
    public bool InActive { get; set; }
}

public class PassengerTypeDiscount
{
    public string PassengerTypeCode { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
}

public class TaxDefinition
{
    public int TaxId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Null = any departure country.</summary>
    public string? DepartureCountryCode { get; set; }
    /// <summary>ALL / INT / DOM.</summary>
    public string Scope { get; set; } = "ALL";
    /// <summary>Pss.Contracts.Booking.ChargeType (5 Tax, 4 TravelFee, 19 FareSurcharge …).</summary>
    public int ChargeType { get; set; } = 5;
    public decimal Amount { get; set; }
    public bool IsPercent { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    /// <summary>Comma separated PTCs that are exempt (e.g. "INF,CHD").</summary>
    public string ExemptPassengerTypes { get; set; } = string.Empty;
    public bool InActive { get; set; }
}

/// <summary>Local copy of station → country (owned by RefData; copied here, blueprint §6.3 "never share databases").</summary>
public class StationCountry
{
    public string StationCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}
