using Pss.Contracts.Booking;

namespace Pss.Contracts.Internal;

public class PaxTypeCount
{
    public string Type { get; set; } = "ADT";
    public int Count { get; set; }
}

public class PriceSegmentInput
{
    public string Ref { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateTime DepartureLocal { get; set; }
    public bool International { get; set; }
    /// <summary>For journey pricing: classes with open seats. For selection pricing: ignored.</summary>
    public Dictionary<string, int> AvailableClasses { get; set; } = new();
    /// <summary>For selection pricing: the fare chosen at shopping time.</summary>
    public long? FareId { get; set; }
    public string? ClassOfService { get; set; }
}

public class PriceJourneyInput
{
    public string Ref { get; set; } = string.Empty;
    public List<PriceSegmentInput> Segments { get; set; } = new();
}

public class PriceJourneysRequest
{
    public string CurrencyCode { get; set; } = "USD";
    public string? PromotionCode { get; set; }
    public List<PaxTypeCount> Passengers { get; set; } = new();
    public List<PriceJourneyInput> Journeys { get; set; } = new();
    public int MaxFaresPerJourney { get; set; } = 1;
}

public class PricedPassengerFare
{
    public string PassengerType { get; set; } = "ADT";
    public decimal FareAmount { get; set; }
    public decimal DiscountedFare { get; set; }
    public decimal TaxesAndFees { get; set; }
    public List<ServiceCharge> ServiceCharges { get; set; } = new();
}

public class PricedSegmentFare
{
    public string SegmentRef { get; set; } = string.Empty;
    public long FareId { get; set; }
    public string FareKey { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string ClassOfService { get; set; } = string.Empty;
    public string FareBasisCode { get; set; } = string.Empty;
    public string RuleNumber { get; set; } = string.Empty;
    public string? ProductClass { get; set; }
    public string? TravelClassCode { get; set; }
    public int FareSequence { get; set; }
    public List<PricedPassengerFare> PassengerFares { get; set; } = new();
}

public class PricedFareOption
{
    public string? ProductClass { get; set; }
    public int AvailableCount { get; set; }
    public List<PricedSegmentFare> Segments { get; set; } = new();
}

public class PricedJourney
{
    public string Ref { get; set; } = string.Empty;
    public List<PricedFareOption> Options { get; set; } = new();
}

public class PriceJourneysResponse
{
    public List<PricedJourney> Journeys { get; set; } = new();
}

public class PriceSelectionRequest
{
    public string CurrencyCode { get; set; } = "USD";
    public string? PromotionCode { get; set; }
    public List<PaxTypeCount> Passengers { get; set; } = new();
    public List<PriceSegmentInput> Segments { get; set; } = new();
}

public class PriceSelectionResponse
{
    public List<PricedSegmentFare> Segments { get; set; } = new();
}
