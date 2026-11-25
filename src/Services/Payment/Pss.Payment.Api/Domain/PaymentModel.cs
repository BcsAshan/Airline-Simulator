namespace Pss.Payment.Api.Domain;

/// <summary>Persisted payment (blueprint §18 Payment DB). Never stores PAN or CVV — only the masked number and a vault token.</summary>
public class PaymentRecord
{
    public long PaymentId { get; set; }
    public long BookingId { get; set; }
    public string RecordLocator { get; set; } = string.Empty;
    public int PaymentNumber { get; set; }
    public string MethodCode { get; set; } = string.Empty;
    public int MethodType { get; set; }
    public int Status { get; set; }
    public int AuthorizationStatus { get; set; }
    public string? AuthorizationCode { get; set; }
    public string? TransactionCode { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string? AccountNumberMasked { get; set; }
    public string? AccountName { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public int Installments { get; set; } = 1;
    public string? VaultToken { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public byte[] RowVer { get; set; } = Array.Empty<byte>();
}

public class PaymentTransaction
{
    public long Id { get; set; }
    public long PaymentId { get; set; }
    /// <summary>Auth / Void / Capture / Refund / 3DS / DCC.</summary>
    public string Type { get; set; } = "Auth";
    public string? PspReference { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ResponseCode { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public sealed record PaymentMethodField(string Code, string Name, int Ordinal, bool Required, int MinLength, int MaxLength, int ValueType);

public sealed record PaymentMethodDefinition(string Code, string Name, int Type, bool AgentOnly, bool IsCard, IReadOnlyList<PaymentMethodField> Fields);

/// <summary>Luhn + card helpers.</summary>
public static class CardRules
{
    public static bool IsValidPan(string? pan)
    {
        if (string.IsNullOrWhiteSpace(pan)) return false;
        var digits = pan.Where(char.IsDigit).ToArray();
        if (digits.Length != pan.Count(c => !char.IsWhiteSpace(c) && c != '-') || digits.Length is < 13 or > 19) return false;
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    public static string Normalize(string pan) => new(pan.Where(char.IsDigit).ToArray());

    public static string Mask(string pan) => new string('X', Math.Max(0, pan.Length - 4)) + pan[^4..];

    /// <summary>Accepts ISO dates ("2030-12-01", "2030-12-01T00:00:00") and "MM/yyyy" / "MM/yy". Returns the last day of the month.</summary>
    public static DateTime? ParseExpiry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        var parts = v.Split('/');
        if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var y) && m is >= 1 and <= 12)
        {
            if (y < 100) y += 2000;
            return new DateTime(y, m, DateTime.DaysInMonth(y, m));
        }
        if (DateTime.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var d))
            return new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        return null;
    }
}
