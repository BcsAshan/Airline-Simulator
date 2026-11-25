using MassTransit;
using Microsoft.EntityFrameworkCore;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Booking;
using Pss.Contracts.Events;
using Pss.Contracts.Internal;
using Pss.Payment.Api.Domain;
using Pss.Payment.Api.Infrastructure;
using PaymentModel = Pss.Contracts.Booking.Payment;

namespace Pss.Payment.Api.Application;

public class AddPaymentRequest
{
    public string PaymentMethodCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public int? Installments { get; set; }
    public Dictionary<string, string>? PaymentFields { get; set; }
}

/// <summary>
/// Payments (blueprint §7.9, §16). Phase 1: cash (PrePaid, agent) and cards via FakeGateway, authorised at commit.
/// PCI: the PAN is tokenised immediately and never stored, logged or put in the booking draft.
/// </summary>
public sealed class PaymentService(
    PaymentDbContext db,
    IPaymentGateway gateway,
    IBookingDraftClient booking,
    PaymentMethodCatalog catalog,
    IPublishEndpoint publisher,
    TimeProvider clock)
{
    public Dictionary<string, object> AvailableMethods(SessionInfo session)
        => catalog.Methods.Values
            .Where(m => !m.AgentOnly || session.IsAgent)
            .ToDictionary(m => m.Code, m => (object)new
            {
                paymentMethodCode = m.Code,
                name = m.Name,
                paymentMethodType = m.Type,
                allowed = true,
                allowDeposit = false,
                allowZeroAmount = false,
                commissionable = false,
                dccType = 0,
                disallowPartialRefund = false,
                feeCode = (string?)null,
                inActive = false,
                maxInstallments = 1,
                paymentMethodFields = m.Fields.ToDictionary(f => f.Code, f => new
                {
                    paymentMethodFieldCode = f.Code,
                    name = f.Name,
                    ordinal = f.Ordinal,
                    paymentMethodCode = m.Code,
                    paymentMethodType = m.Type,
                    requiredType = f.Required ? 1 : 0,
                    valueMinLength = f.MinLength,
                    valueMaxLength = f.MaxLength,
                    valueType = f.ValueType
                })
            });

    /// <summary>POST booking/payments — validates, tokenises and adds the payment to the booking in state.</summary>
    public async Task<string> AddToBookingAsync(SessionInfo session, AddPaymentRequest request, CancellationToken ct)
    {
        if (!catalog.Methods.TryGetValue(request.PaymentMethodCode ?? string.Empty, out var method) || (method.AgentOnly && !session.IsAgent))
            throw new DomainException(ErrorCodes.InvalidPaymentMethod, $"Payment method '{request.PaymentMethodCode}' is not available.");
        if (request.Amount <= 0)
            throw new DomainException(ErrorCodes.InvalidPaymentAmount, "The payment amount must be greater than zero.");

        var summary = await booking.GetSummaryAsync(ct);
        var currency = (request.CurrencyCode ?? summary.CurrencyCode).Trim().ToUpperInvariant();
        if (currency != summary.CurrencyCode)
            throw new DomainException(ErrorCodes.InvalidCurrencyCode, "Paying in another currency (MCC/DCC) is delivered in Phase 4.");
        if (request.Amount > summary.BalanceDue)
            throw new DomainException(ErrorCodes.InvalidPaymentAmount, $"The amount exceeds the balance due ({summary.BalanceDue} {summary.CurrencyCode}).");

        var now = clock.GetUtcNow().UtcDateTime;
        var payment = new PaymentModel
        {
            Code = method.Code,
            Type = (PaymentMethodType)method.Type,
            Amounts = new PaymentAmounts
            {
                Amount = request.Amount, CurrencyCode = currency,
                Collected = request.Amount, CollectedCurrencyCode = currency,
                Quoted = request.Amount, QuotedCurrencyCode = currency
            },
            Details = new PaymentDetails { Installments = Math.Max(1, request.Installments ?? 1) },
            CreatedDate = now
        };

        if (method.IsCard)
        {
            var fields = new Dictionary<string, string>(request.PaymentFields ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var f in method.Fields.Where(f => f.Required))
                if (!fields.TryGetValue(f.Code, out var v) || string.IsNullOrWhiteSpace(v))
                    throw new DomainException(ErrorCodes.PaymentFieldRequired, $"Payment field '{f.Code}' is required.", 400, new { field = f.Code });

            var pan = CardRules.Normalize(fields["ACCTNO"]);
            if (!CardRules.IsValidPan(fields["ACCTNO"]))
                throw new DomainException(ErrorCodes.InvalidAccountNumber, "The card number is not valid.");
            var expiry = CardRules.ParseExpiry(fields["EXPDAT"])
                         ?? throw new DomainException(ErrorCodes.CardExpired, "The expiry date is not valid.");
            if (expiry.Date < now.Date)
                throw new DomainException(ErrorCodes.CardExpired, "The card has expired.");

            payment.Status = BookingPaymentStatus.Received;
            payment.AuthorizationStatus = AuthorizationStatus.Unknown;
            payment.Details.AccountNumber = CardRules.Mask(pan);
            payment.Details.AccountName = fields["CC::AccountHolderName"].Trim();
            payment.Details.ExpirationDate = expiry;
            payment.Details.BinRange = int.Parse(pan[..6]);
            payment.Details.Fields["PMTOKEN"] = gateway.Tokenize(pan); // PAN/CVV dropped here
        }
        else if (payment.Type == PaymentMethodType.PrePaid)
        {
            payment.Status = BookingPaymentStatus.Approved; // cash is approved immediately (persisted at commit)
            payment.AuthorizationStatus = AuthorizationStatus.Approved;
            payment.ApprovalDate = now;
        }
        else
        {
            throw new DomainException(ErrorCodes.NotSupportedYet, "Credit shell, voucher and agency payments are delivered in Phase 2/4.");
        }

        return await booking.AddPaymentAsync(new AddDraftPaymentRequest { Payment = payment }, ct);
    }

    /// <summary>Internal (commit): authorise draft payments. Idempotent per bookingId + draft payment key.</summary>
    public async Task<AuthorizePaymentsResponse> AuthorizeAsync(AuthorizePaymentsRequest request, CancellationToken ct)
    {
        var response = new AuthorizePaymentsResponse();
        foreach (var p in request.Payments)
        {
            var idempotencyKey = $"{request.BookingId}:{p.PaymentKey}";
            var previous = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, ct);
            if (previous is not null)
            {
                var existing = await db.Payments.AsNoTracking().FirstAsync(x => x.PaymentId == previous.PaymentId, ct);
                response.Results.Add(ToResult(p.PaymentKey, existing, null));
                continue;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var record = new PaymentRecord
            {
                BookingId = request.BookingId,
                RecordLocator = request.RecordLocator,
                PaymentNumber = Pss.BuildingBlocks.Keys.PaymentKey.Parse(p.PaymentKey).Sequence,
                MethodCode = p.Code,
                MethodType = (int)p.Type,
                Amount = p.Amounts.Amount,
                CurrencyCode = p.Amounts.CurrencyCode,
                AccountNumberMasked = p.Details.AccountNumber,
                AccountName = p.Details.AccountName,
                ExpirationDate = p.Details.ExpirationDate,
                Installments = p.Details.Installments,
                VaultToken = p.Details.Fields.GetValueOrDefault("PMTOKEN"),
                CreatedUtc = now
            };

            string? declineReason = null;
            string pspReference;
            string responseCode;
            if (p.Type == PaymentMethodType.PrePaid)
            {
                record.Status = (int)BookingPaymentStatus.Approved;
                record.AuthorizationStatus = (int)AuthorizationStatus.Approved;
                record.ApprovedUtc = now;
                pspReference = "CASH";
                responseCode = "00";
            }
            else if (p.Type == PaymentMethodType.ExternalAccount && !string.IsNullOrEmpty(record.VaultToken))
            {
                var auth = await gateway.AuthorizeAsync(record.VaultToken, record.Amount, record.CurrencyCode, idempotencyKey, ct);
                record.Status = (int)(auth.Approved ? BookingPaymentStatus.Approved : BookingPaymentStatus.Declined);
                record.AuthorizationStatus = (int)(auth.Approved ? AuthorizationStatus.Approved : AuthorizationStatus.Declined);
                record.AuthorizationCode = auth.AuthorizationCode;
                record.TransactionCode = auth.PspReference;
                record.ApprovedUtc = auth.Approved ? now : null;
                pspReference = auth.PspReference;
                responseCode = auth.ResponseCode;
                declineReason = auth.DeclineReason;
            }
            else
            {
                throw new DomainException(ErrorCodes.NotSupportedYet, $"Payment type {p.Type} cannot be authorised yet.");
            }

            db.Payments.Add(record);
            await db.SaveChangesAsync(ct); // assigns PaymentId

            db.Transactions.Add(new PaymentTransaction
            {
                PaymentId = record.PaymentId, Type = "Auth", PspReference = pspReference, ResponseCode = responseCode,
                Status = ((BookingPaymentStatus)record.Status).ToString(), IdempotencyKey = idempotencyKey, CreatedUtc = now
            });
            if (record.Status == (int)BookingPaymentStatus.Approved)
                await publisher.Publish(new PaymentApproved(record.BookingId, record.RecordLocator, record.PaymentId, record.MethodCode,
                    record.Amount, record.CurrencyCode, now), ct);
            else
                await publisher.Publish(new PaymentDeclined(record.BookingId, record.RecordLocator, record.PaymentId, record.MethodCode,
                    record.Amount, record.CurrencyCode, declineReason, now), ct);
            await db.SaveChangesAsync(ct); // transaction log + outbox

            response.Results.Add(ToResult(p.PaymentKey, record, declineReason));
        }
        return response;
    }

    /// <summary>Internal: compensation when a commit fails after authorisation.</summary>
    public async Task VoidAsync(VoidPaymentsRequest request, CancellationToken ct)
    {
        var records = await db.Payments.Where(p => request.PaymentIds.Contains(p.PaymentId)
                                                   && p.Status == (int)BookingPaymentStatus.Approved).ToListAsync(ct);
        foreach (var r in records)
        {
            if (r.MethodType == (int)PaymentMethodType.ExternalAccount && r.TransactionCode is not null)
                await gateway.VoidAsync(r.TransactionCode, ct);
            r.Status = (int)BookingPaymentStatus.Declined;
            r.AuthorizationStatus = (int)AuthorizationStatus.Voided;
            db.Transactions.Add(new PaymentTransaction
            {
                PaymentId = r.PaymentId, Type = "Void", PspReference = r.TransactionCode, Status = "Voided",
                ResponseCode = request.Reason.Length > 32 ? request.Reason[..32] : request.Reason,
                IdempotencyKey = $"void:{r.PaymentId}:{Guid.NewGuid():N}", CreatedUtc = clock.GetUtcNow().UtcDateTime
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static PaymentAuthResult ToResult(string draftKey, PaymentRecord r, string? declineReason) => new()
    {
        DraftPaymentKey = draftKey,
        PaymentId = r.PaymentId,
        Status = (BookingPaymentStatus)r.Status,
        AuthorizationStatus = (AuthorizationStatus)r.AuthorizationStatus,
        AuthorizationCode = r.AuthorizationCode,
        TransactionCode = r.TransactionCode,
        ApprovalDate = r.ApprovedUtc is null ? null : DateTime.SpecifyKind(r.ApprovedUtc.Value, DateTimeKind.Utc),
        DeclineReason = declineReason
    };
}
