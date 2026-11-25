using System.Security.Cryptography;

namespace Pss.Payment.Api.Infrastructure;

public sealed record AuthorizationResult(bool Approved, string? AuthorizationCode, string PspReference, string ResponseCode, string? DeclineReason);

/// <summary>PSP abstraction (blueprint §16). Phase 4 adds 3DS2, DCC, capture and refunds + a real adapter (Adyen/Stripe/…).</summary>
public interface IPaymentGateway
{
    /// <summary>Exchanges a PAN for an opaque vault token (in production: hosted fields / PSP tokenisation at the edge).</summary>
    string Tokenize(string pan);
    Task<AuthorizationResult> AuthorizeAsync(string token, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct);
    Task VoidAsync(string pspReference, CancellationToken ct);
}

/// <summary>
/// Deterministic fake PSP. Test cards: 4000000000000002 → declined; 4000000000009995 → declined (insufficient funds);
/// any other Luhn-valid card → approved.
/// </summary>
public sealed class FakePaymentGateway(ILogger<FakePaymentGateway> logger) : IPaymentGateway
{
    public string Tokenize(string pan)
    {
        var outcome = pan switch
        {
            "4000000000000002" => "decline",
            "4000000000009995" => "nsf",
            _ => "approve"
        };
        return $"fk_{outcome}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
    }

    public Task<AuthorizationResult> AuthorizeAsync(string token, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct)
    {
        var psp = "FAKE-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        AuthorizationResult result;
        if (token.StartsWith("fk_decline_", StringComparison.Ordinal))
            result = new AuthorizationResult(false, null, psp, "05", "Do not honour");
        else if (token.StartsWith("fk_nsf_", StringComparison.Ordinal))
            result = new AuthorizationResult(false, null, psp, "51", "Insufficient funds");
        else
            result = new AuthorizationResult(true, RandomNumberGenerator.GetInt32(100000, 999999).ToString(), psp, "00", null);
        logger.LogInformation("FakeGateway authorise {Amount} {Currency}: {Result}", amount, currencyCode, result.Approved ? "approved" : "declined");
        return Task.FromResult(result);
    }

    public Task VoidAsync(string pspReference, CancellationToken ct)
    {
        logger.LogInformation("FakeGateway void {PspReference}", pspReference);
        return Task.CompletedTask;
    }
}
