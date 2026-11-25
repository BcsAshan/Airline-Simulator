using System.Text.Json.Serialization;

namespace Pss.Contracts.Booking;

// Public Booking aggregate contract (blueprint §9), field names verified against HAR responses.
// Mutable POCOs on purpose: the Booking service mutates the draft ("booking in state") directly.

public class Booking
{
    public bool SelfServiceMoveAvailable { get; set; }
    public string? BookingKey { get; set; }
    public string? RecordLocator { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string? SystemCode { get; set; }
    public string? GroupName { get; set; }
    public BookingLocators Locators { get; set; } = new();
    public BookingInfo Info { get; set; } = new();
    public BookingSales Sales { get; set; } = new();
    public TypeOfSale TypeOfSale { get; set; } = new();
    public BookingHold? Hold { get; set; }
    public BookingBreakdown Breakdown { get; set; } = new();
    public ReceivedBy? ReceivedBy { get; set; }
    public Dictionary<string, Contact> Contacts { get; set; } = new();
    public Dictionary<string, Passenger> Passengers { get; set; } = new();
    public List<Journey> Journeys { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<BookingComment> Comments { get; set; } = new();
    public List<object> Queues { get; set; } = new();
    public List<BookingHistoryItem> History { get; set; } = new();
    public Dictionary<string, object> AddOns { get; set; } = new();
    public List<object> Orders { get; set; } = new();
}

public class BookingLocators
{
    public string? NumericRecordLocator { get; set; }
    public string? ParentRecordLocator { get; set; }
    public long ParentId { get; set; }
    public List<RecordLocator> RecordLocators { get; set; } = new();
}

public class RecordLocator
{
    public string? RecordLocatorKey { get; set; }
    public string? RecordCode { get; set; }
    public string? SystemDomainCode { get; set; }
    public string? OwningSystemCode { get; set; }
    public string? BookingSystemCode { get; set; }
    public string? InteractionPurpose { get; set; }
    public string? HostedCarrierCode { get; set; }
}

public class BookingInfo
{
    public BookingStatus Status { get; set; }
    public PaidStatus PaidStatus { get; set; }
    public PriceStatus PriceStatus { get; set; } = PriceStatus.Valid;
    public int ProfileStatus { get; set; }
    public string? BookingType { get; set; }
    public ChannelType ChannelType { get; set; }
    public DateTime? BookedDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public long ModifiedAgentId { get; set; }
    public long CreatedAgentId { get; set; }
    public string? OwningCarrierCode { get; set; }
    public bool ChangeAllowed { get; set; } = true;
}

public class PointOfSale
{
    public string? AgentCode { get; set; }
    public string? DomainCode { get; set; }
    public string? LocationCode { get; set; }
    public string? OrganizationCode { get; set; }
}

public class SourcePointOfSale : PointOfSale
{
    public string? IsoCountryCode { get; set; }
    public string? SourceSystemCode { get; set; }
}

public class BookingSales
{
    public PointOfSale? Created { get; set; }
    public SourcePointOfSale? Source { get; set; }
    public PointOfSale? Modified { get; set; }
}

public class TypeOfSale
{
    public string? ResidentCountry { get; set; }
    public string? PromotionCode { get; set; }
    public List<string> FareTypes { get; set; } = new();
}

public class BookingHold
{
    public DateTime? Expiration { get; set; }
}

public class ReceivedBy
{
    [JsonPropertyName("receivedBy")]
    public string? ReceivedByName { get; set; }
    public string? LatestReceivedBy { get; set; }
    public string? ReceivedReference { get; set; }
    public string? LatestReceivedReference { get; set; }
    public string? ReferralCode { get; set; }
}

public class BookingComment
{
    public string? CommentKey { get; set; }
    public CommentType Type { get; set; }
    public string? Text { get; set; }
    public DateTime CreatedDate { get; set; }
    public PointOfSale? PointOfSale { get; set; }
}

public class BookingHistoryItem
{
    public DateTime CreatedDate { get; set; }
    public string? Event { get; set; }
    public string? HistoryCategory { get; set; }
    public string? Details { get; set; }
    public PointOfSale? PointOfSale { get; set; }
    public string? ReceivedBy { get; set; }
}
