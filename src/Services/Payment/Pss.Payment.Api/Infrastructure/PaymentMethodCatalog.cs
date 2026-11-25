using Pss.Payment.Api.Domain;
using Pss.SeedData;

namespace Pss.Payment.Api.Infrastructure;

/// <summary>
/// Payment method configuration (codes and field metadata). Phase 1 loads the demo carrier definitions;
/// TODO(Phase 4): read from RefData resources/paymentMethods and cache with ETag.
/// </summary>
public sealed class PaymentMethodCatalog
{
    private static readonly IReadOnlyList<PaymentMethodField> CardFields = new List<PaymentMethodField>
    {
        new("ACCTNO", "AccountNumber", 1, true, 13, 19, 3),
        new("EXPDAT", "Expiry Date", 2, true, 0, 0, 6),
        new("CC::AccountHolderName", "AccountHolderName", 3, true, 1, 30, 8),
        new("CC::VerificationCode", "CVV", 4, true, 3, 4, 3)
    };

    public IReadOnlyDictionary<string, PaymentMethodDefinition> Methods { get; } = DemoCarrier.PaymentMethods
        .ToDictionary(m => m.Code, m => new PaymentMethodDefinition(m.Code, m.Name, m.Type, m.AgentOnly, m.IsCard,
            m.IsCard ? CardFields : Array.Empty<PaymentMethodField>()), StringComparer.OrdinalIgnoreCase);
}
