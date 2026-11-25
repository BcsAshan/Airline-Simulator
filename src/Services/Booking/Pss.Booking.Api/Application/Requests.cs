using Pss.Contracts.Booking;
using Pss.Contracts.Internal;

namespace Pss.Booking.Api.Application;

// Request models (field names match the Navitaire Postman collections / HAR files).

public class SellKey
{
    public string JourneyKey { get; set; } = string.Empty;
    public string FareAvailabilityKey { get; set; } = string.Empty;
    public string? StandbyPriorityCode { get; set; }
    public string? InventoryControl { get; set; }
}

public class SellPassengers
{
    public List<PaxTypeCount> Types { get; set; } = new();
}

/// <summary>POST /api/nsk/v4/trip/sell</summary>
public class TripSellRequest
{
    public List<SellKey> Keys { get; set; } = new();
    public SellPassengers? Passengers { get; set; }
    public string? CurrencyCode { get; set; }
    public string? PromotionCode { get; set; }
    public int InfantCount { get; set; }
    public bool? PreventOverlap { get; set; }
    public bool SuppressPassengerAgeValidation { get; set; }
    public string? SourceOrganization { get; set; }
}

public class TripJourneys
{
    public List<SellKey> Keys { get; set; } = new();
}

public class TripPassengerWrapper
{
    public PassengerUpdateRequest? Passenger { get; set; }
    public object? Infant { get; set; }
}

/// <summary>POST /api/nsk/v4|v5/trip — one-shot booking (journeys + passengers + contact).</summary>
public class TripRequest
{
    public TripJourneys? Journeys { get; set; }
    public List<TripPassengerWrapper> Passengers { get; set; } = new();
    public Contact? Contact { get; set; }
    public List<Contact>? Contacts { get; set; }
    public string? CurrencyCode { get; set; }
    public string? PromotionCode { get; set; }
}

public class PassengerInfoRequest
{
    public string? Nationality { get; set; }
    public string? ResidentCountry { get; set; }
    public Gender? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? FamilyNumber { get; set; }
    public string? DeclaredGender { get; set; }
}

/// <summary>PUT/PATCH /api/nsk/v3/booking/passengers/{passengerKey}</summary>
public class PassengerUpdateRequest
{
    public Name? Name { get; set; }
    public PassengerInfoRequest? Info { get; set; }
    public string? PassengerTypeCode { get; set; }
    public string? CustomerNumber { get; set; }
    public string? DiscountCode { get; set; }
    public PassengerProgram? Program { get; set; }
}

public class CommitComment
{
    public CommentType Type { get; set; }
    public string? Text { get; set; }
}

/// <summary>POST|PUT /api/nsk/v3/booking (commit). Commit can also add comments and set received-by.</summary>
public class CommitRequest
{
    public string? ReceivedBy { get; set; }
    public List<CommitComment>? Comments { get; set; }
    public bool? NotifyContacts { get; set; }
    public DistributionOption? DistributionOption { get; set; }
}

/// <summary>GET /api/nsk/v2/booking/retrieve query (strategy-based).</summary>
public class RetrieveQuery
{
    public string? RecordLocator { get; set; }
    public string? LastName { get; set; }
    public string? FirstName { get; set; }
    public string? EmailAddress { get; set; }
}
