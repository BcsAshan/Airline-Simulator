using Pss.Contracts.Booking;
using Pss.Contracts.Internal;

namespace Pss.Shopping.Api.Models;

// ---------- requests (POST v4/availability/search/simple and v4/availability/search) ----------

public class PassengerTypeCriteria
{
    public List<PaxTypeCount> Types { get; set; } = new();
}

public class SearchCodes
{
    public string? PromotionCode { get; set; }
    public string? CurrencyCode { get; set; }
}

public class SearchFilters
{
    public int? MaxConnections { get; set; }
    public FlightType? FlightType { get; set; }
    public string? CarrierCode { get; set; }
}

public class SimpleSearchRequest
{
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool? SearchOriginMacs { get; set; }
    public bool? SearchDestinationMacs { get; set; }
    public DateTime BeginDate { get; set; }
    public DateTime? EndDate { get; set; }
    public PassengerTypeCriteria? Passengers { get; set; }
    public string? CurrencyCode { get; set; }
    public SearchCodes? Codes { get; set; }
    public SearchFilters? Filters { get; set; }
    public int? NumberOfFaresPerJourney { get; set; }
    public TaxesAndFeesRollupMode? TaxesAndFees { get; set; }
}

public class SearchStations
{
    public List<string> OriginStationCodes { get; set; } = new();
    public List<string> DestinationStationCodes { get; set; } = new();
}

public class SearchDates
{
    public DateTime BeginDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class SearchCriterion
{
    public SearchStations Stations { get; set; } = new();
    public SearchDates Dates { get; set; } = new();
    public SearchFilters? Filters { get; set; }
}

public class FullSearchRequest
{
    public List<SearchCriterion> Criteria { get; set; } = new();
    public PassengerTypeCriteria? Passengers { get; set; }
    public SearchCodes? Codes { get; set; }
    public int? NumberOfFaresPerJourney { get; set; }
    public TaxesAndFeesRollupMode? TaxesAndFees { get; set; }
}

// ---------- response (shape verified against HAR bookingFlowWithRest) ----------

public class AvailabilityResponse
{
    public List<AvailabilityResult> Results { get; set; } = new();
    public Dictionary<string, FareAvailability> FaresAvailable { get; set; } = new();
    public string CurrencyCode { get; set; } = "USD";
    public bool IncludeTaxesAndFees { get; set; }
    public object? BundleOffers { get; set; }
}

public class AvailabilityResult
{
    public List<AvailabilityTrip> Trips { get; set; } = new();
}

public class AvailabilityTrip
{
    public bool MultipleOriginStations { get; set; }
    public bool MultipleDestinationStations { get; set; }
    public DateTime Date { get; set; }
    public Dictionary<string, List<AvailableJourney>> JourneysAvailableByMarket { get; set; } = new();
}

public class AvailableJourney
{
    public FlightType FlightType { get; set; }
    public int Stops { get; set; }
    public Designator Designator { get; set; } = new();
    public string JourneyKey { get; set; } = string.Empty;
    public List<AvailableSegment> Segments { get; set; } = new();
    public List<JourneyFare> Fares { get; set; } = new();
    public bool NotForGeneralUser { get; set; }
}

public class AvailableSegment
{
    public bool IsChangeOfGauge { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsHosted { get; set; } = true;
    public Designator Designator { get; set; } = new();
    public bool IsSeatmapViewable { get; set; } = true;
    public string SegmentKey { get; set; } = string.Empty;
    public FlightIdentifier Identifier { get; set; } = new();
    public string? CabinOfService { get; set; }
    public FlightIdentifier? ExternalIdentifier { get; set; }
    public bool International { get; set; }
    public int SegmentType { get; set; }
    public List<Leg> Legs { get; set; } = new();
}

public class JourneyFare
{
    public string FareAvailabilityKey { get; set; } = string.Empty;
    public List<JourneyFareDetail> Details { get; set; } = new();
    public bool IsSumOfSector { get; set; }
}

public class JourneyFareDetail
{
    public int AvailableCount { get; set; }
    public int Status { get; set; } = 1;
    public string Reference { get; set; } = string.Empty;
    public string? ServiceBundleSetCode { get; set; }
    public object? BundleReferences { get; set; }
    public object? SsrReferences { get; set; }
}

public class FareAvailability
{
    public FareTotals Totals { get; set; } = new();
    public bool IsSumOfSector { get; set; }
    public string FareAvailabilityKey { get; set; } = string.Empty;
    public List<AvailableFare> Fares { get; set; } = new();
}

public class FareTotals
{
    public decimal FareTotal { get; set; }
    public decimal RevenueTotal { get; set; }
    public decimal PublishedTotal { get; set; }
    public decimal LoyaltyTotal { get; set; }
    public decimal DiscountedTotal { get; set; }
}

public class AvailableFare
{
    public bool IsGoverning { get; set; } = true;
    public string FareBasisCode { get; set; } = string.Empty;
    public string ClassOfService { get; set; } = string.Empty;
    public string? ClassType { get; set; }
    public int FareApplicationType { get; set; }
    public int FareStatus { get; set; }
    public string? ProductClass { get; set; }
    public string? RuleNumber { get; set; }
    public string? RuleTariff { get; set; }
    public string? TravelClassCode { get; set; }
    public bool IsAllotmentMarketFare { get; set; }
    public string Reference { get; set; } = string.Empty;
    public List<AvailablePassengerFare> PassengerFares { get; set; } = new();
}

public class AvailablePassengerFare
{
    public string? FareDiscountCode { get; set; }
    public string? PassengerDiscountCode { get; set; }
    public string PassengerType { get; set; } = "ADT";
    public decimal FareAmount { get; set; }
    public decimal RevenueFare { get; set; }
    public decimal PublishedFare { get; set; }
    public decimal LoyaltyPoints { get; set; }
    public decimal DiscountedFare { get; set; }
    public List<ServiceCharge> ServiceCharges { get; set; } = new();
    public int Multiplier { get; set; } = 1;
}
