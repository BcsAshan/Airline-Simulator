namespace Pss.Contracts.Booking;

public class Payment
{
    public string PaymentKey { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime? ApprovalDate { get; set; }
    public PaymentDetails Details { get; set; } = new();
    public PaymentAmounts Amounts { get; set; } = new();
    public string? AuthorizationCode { get; set; }
    public AuthorizationStatus AuthorizationStatus { get; set; }
    public DateTime? FundedDate { get; set; }
    public string? TransactionCode { get; set; }
    public object? Dcc { get; set; }
    public object? ThreeDSecure { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public PaymentMethodType Type { get; set; }
    public BookingPaymentStatus Status { get; set; }
    public bool Transferred { get; set; }
    public ChannelType ChannelType { get; set; }
    public PointOfSale? PointOfSale { get; set; }
    public PointOfSale? SourcePointOfSale { get; set; }
    public bool Deposit { get; set; }
    public bool AddedToState { get; set; }
    public string? Reference { get; set; }
}

public class PaymentDetails
{
    public long AccountNumberId { get; set; }
    public long? ParentPaymentId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountNumber { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string? Text { get; set; }
    public int Installments { get; set; } = 1;
    public int BinRange { get; set; }
    /// <summary>Non-sensitive fields only (never PAN/CVV). PMTOKEN holds the vault token.</summary>
    public Dictionary<string, string> Fields { get; set; } = new();
}

public class PaymentAmounts
{
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal Collected { get; set; }
    public string CollectedCurrencyCode { get; set; } = "USD";
    public decimal Quoted { get; set; }
    public string QuotedCurrencyCode { get; set; } = "USD";
}
