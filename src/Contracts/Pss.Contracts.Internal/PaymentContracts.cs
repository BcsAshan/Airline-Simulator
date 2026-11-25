using Pss.Contracts.Booking;

namespace Pss.Contracts.Internal;

/// <summary>Booking -> Payment during commit: authorise draft payments that have not been processed yet.</summary>
public class AuthorizePaymentsRequest
{
    public long BookingId { get; set; }
    public string RecordLocator { get; set; } = string.Empty;
    public List<Payment> Payments { get; set; } = new();
}

public class PaymentAuthResult
{
    public string DraftPaymentKey { get; set; } = string.Empty;
    public long PaymentId { get; set; }
    public BookingPaymentStatus Status { get; set; }
    public AuthorizationStatus AuthorizationStatus { get; set; }
    public string? AuthorizationCode { get; set; }
    public string? TransactionCode { get; set; }
    public DateTime? ApprovalDate { get; set; }
    public string? DeclineReason { get; set; }
}

public class AuthorizePaymentsResponse
{
    public List<PaymentAuthResult> Results { get; set; } = new();
}

public class VoidPaymentsRequest
{
    public List<long> PaymentIds { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Payment -> Booking: snapshot of the booking in state needed to validate a payment.</summary>
public class DraftSummary
{
    public long? BookingId { get; set; }
    public string? RecordLocator { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal BalanceDue { get; set; }
    public decimal TotalAmount { get; set; }
    public List<Payment> Payments { get; set; } = new();
}

/// <summary>Payment -> Booking: add a validated payment to the booking in state. Booking assigns the key.</summary>
public class AddDraftPaymentRequest
{
    public Payment Payment { get; set; } = new();
}

public class AddDraftPaymentResponse
{
    public string PaymentKey { get; set; } = string.Empty;
}
