namespace Pss.BuildingBlocks.Domain;

/// <summary>
/// Error code registry (blueprint §21). Format: namespace:Area:Reason.
/// Codes marked [DESIGN] are ours (no Navitaire equivalent documented).
/// </summary>
public static class ErrorCodes
{
    // core
    public const string NullRequest = "core:Validation:NullRequest";
    public const string MalformedRequest = "core:Validation:MalformedRequest";
    public const string InvalidJsonSchema = "core:Validation:InvalidJsonSchema";
    public const string EnumOutOfRange = "core:Validation:EnumOutOfRange";
    public const string ResourceNotFound = "core:Validation:ResourceNotFound";
    public const string Unauthorized = "core:Unauthorized";
    public const string UnhandledException = "core:Exception";
    public const string SessionConcurrentRequest = "core:Session:ConcurrentRequest";      // [DESIGN] 409
    public const string ServiceUnavailable = "core:ServiceUnavailable";                   // [DESIGN] 503 downstream

    // nsk
    public const string InvalidKey = "nsk:Exceptions:InvalidKey";
    public const string AgentOnly = "nsk:Authorization:AgentOnly";                         // [DESIGN]
    public const string NoBookingInState = "nsk:Booking:NoBookingInState";
    public const string GeneralRestriction = "nsk:Booking:GeneralRestriction";
    public const string FailedFareRestriction = "nsk:Booking:FailedFareRestriction";
    public const string InvalidStationCode = "nsk:Booking:InvalidStationCode";
    public const string ModifiedByAnotherSession = "nsk:Booking:ModifiedByAnotherSession"; // [DESIGN] 409
    public const string PriceChanged = "nsk:Booking:PriceChanged";                         // [DESIGN] message
    public const string MissingPrimaryContact = "nsk:Booking:MissingPrimaryContact";       // [DESIGN]
    public const string ContactTypeExists = "nsk:Booking:ContactTypeExists";               // [DESIGN]
    public const string ContactNotFound = "nsk:Booking:ContactNotFound";                   // [DESIGN]
    public const string PassengerTypesMismatch = "nsk:Booking:PassengerTypesMismatch";     // [DESIGN]
    public const string JourneyAlreadySold = "nsk:Booking:JourneyAlreadySold";             // [DESIGN]
    public const string JourneyOverlap = "nsk:Booking:JourneyOverlap";                     // [DESIGN]
    public const string NoJourneys = "nsk:Booking:NoJourneys";                             // [DESIGN]
    public const string BalanceDueNotAllowed = "nsk:Booking:BalanceDueNotAllowed";         // [DESIGN]
    public const string JourneyNotChangeable = "nsk:Booking:JourneyNotChangeable";         // [DESIGN]
    public const string NotSupportedYet = "nsk:Booking:NotSupportedYet";                   // [DESIGN] feature in a later phase
    public const string MaxBookingPassengers = "nsk:Availability:MaxBookingPassengers";
    public const string MaxInfantCount = "nsk:Availability:MaxInfantCountToPassengerCount";
    public const string ClassNotAvailable = "nsk:Availability:ClassNotAvailable";          // [DESIGN]
    public const string UnknownSellCurrencyCode = "nsk:Validation:UnknownSellCurrencyCode";
    public const string InvalidFirstNameCharacters = "nsk:Validation:InvalidFirstNameCharacters";
    public const string InvalidLastNameCharacters = "nsk:Validation:InvalidLastNameCharacters";
    public const string InvalidMiddleNameCharacters = "nsk:Validation:InvalidMiddleNameCharacters";
    public const string PassengerTypeNotFound = "nsk:Passenger:TypeNotFound";
    public const string InvalidDateOfBirth = "nsk:Passenger:InvalidDateOfBirth";
    public const string InvalidPassengerType = "nsk:Passenger:InvalidPassengerType";
    public static string PassengerFieldRequired(string property) => $"nsk:Passenger:{property}Required";
    public const string InvalidCurrencyCode = "nsk:Currency:InvalidCurrencyCode";

    // payments
    public const string PaymentKeyNotFound = "nsk:Payment:PaymentKeyNotFoundOnBooking";
    public const string InvalidPaymentMethod = "nsk:Payment:InvalidPaymentMethod";         // [DESIGN]
    public const string PaymentFieldRequired = "nsk:Payment:PaymentFieldRequired";         // [DESIGN]
    public const string InvalidAccountNumber = "nsk:Payment:InvalidAccountNumber";         // [DESIGN]
    public const string CardExpired = "nsk:Payment:CardExpired";                           // [DESIGN]
    public const string InvalidPaymentAmount = "nsk:Payment:InvalidAmount";                // [DESIGN]
    public const string PaymentDeclined = "nsk:Payment:Declined";                          // [DESIGN]
    public const string PaymentNotRemovable = "nsk:Payment:NotRemovable";                  // [DESIGN]

    // identity
    public const string PasswordExpired = "nsk-server:PasswordExpired";
    public const string InvalidCredentials = "nsk:Authentication:InvalidCredentials";      // [DESIGN]
    public const string InvalidApplicationName = "nsk:Authentication:InvalidApplication";  // [DESIGN]
}
