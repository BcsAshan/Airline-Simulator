namespace Pss.Contracts.Events;

// Integration events (blueprint §17). Additive changes only; never rename/remove members.
// MassTransit supplies the envelope (MessageId, CorrelationId, SentTime, headers).

// ---- identity ----
public record SessionCreated(string SessionId, int UserType, string? AgentCode, string? ApplicationName, DateTime CreatedUtc);
public record SessionExpired(string SessionId, string Reason, DateTime ExpiredUtc);

// ---- refdata ----
public record ReferenceDataChanged(string Resource, DateTime ChangedUtc);

// ---- inventory ----
public record InventoryHoldsReleased(string SessionId, IReadOnlyList<long> HoldIds, string Reason, DateTime ReleasedUtc);
public record LegClassAvailabilityChanged(IReadOnlyList<long> LegIds, DateTime ChangedUtc);

// ---- booking ----
public record BookedSegment(string CarrierCode, string FlightNumber, string Origin, string Destination,
    DateTime DepartureLocal, DateTime ArrivalLocal, string ClassOfService);

public record BookedPassenger(string PassengerKey, string PassengerTypeCode, string? FirstName, string? LastName);

public record BookingCommitted(
    long BookingId,
    string RecordLocator,
    int Status,
    string CurrencyCode,
    decimal TotalAmount,
    decimal BalanceDue,
    bool IsNewBooking,
    string? ContactEmail,
    string? ContactFirstName,
    string? ContactLastName,
    int DistributionOption,
    IReadOnlyList<BookedSegment> Segments,
    IReadOnlyList<BookedPassenger> Passengers,
    DateTime OccurredUtc);

// ---- payment ----
public record PaymentApproved(long BookingId, string RecordLocator, long PaymentId, string MethodCode, decimal Amount, string CurrencyCode, DateTime OccurredUtc);
public record PaymentDeclined(long BookingId, string RecordLocator, long PaymentId, string MethodCode, decimal Amount, string CurrencyCode, string? Reason, DateTime OccurredUtc);
