using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed class PaymentResponse
{
    public string PaymentDetailId { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string? OrderId { get; init; }

    /// <summary>
    /// The effective request/operation scope this payment was made under -- which organization's
    /// request this is, echoed from <c>MakePaymentRequest.OrganizationId</c> (or resolved from
    /// the caller's own context when the request named none).
    /// </summary>
    /// <remarks>
    /// This is <em>not</em> which <see cref="Entities.PaymentProvider"/> configuration row the
    /// scope-fallback chain actually resolved to execute the payment -- a tenant-wide or
    /// console-level configuration can serve a request scoped to a different organization
    /// entirely. A caller that needs to know the actual provider configuration used must read
    /// <see cref="ResolvedProviderId"/> and <see cref="ResolvedProviderOrganizationId"/> instead;
    /// comparing this field against an expected provider scope was a defect (see PR #393) that
    /// produced false-positive scope-mismatch failures whenever provider resolution fell through
    /// to a shared configuration.
    /// </remarks>
    public string? OrganizationId { get; init; }

    /// <summary>
    /// The item id of the exact <see cref="Entities.PaymentProvider"/> row the scope-fallback
    /// chain resolved and executed this payment against.
    /// </summary>
    /// <remarks>
    /// The identity a caller should compare against an expected provider -- e.g. one frozen
    /// earlier onto a billing account -- rather than <see cref="OrganizationId"/>, which
    /// describes the request scope and not the provider row.
    /// </remarks>
    public string? ResolvedProviderId { get; init; }

    /// <summary>
    /// The real scope of the resolved <see cref="Entities.PaymentProvider"/> row: null when it is
    /// a tenant-level configuration, an organization id when it is scoped to one. Never coerced to
    /// the caller's own organization -- a null here is meaningful and must stay null.
    /// </summary>
    public string? ResolvedProviderOrganizationId { get; init; }
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string? RedirectUrl { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public string? CheckoutSessionStatus { get; init; }
    public string? CheckoutResultCode { get; init; }
    public PaymentInstrumentResponse? PaymentInstrument { get; init; }
    public string? PaymentFlow { get; init; }
    public string? RecurringProcessingModel { get; init; }
    public string? CaptureStatus { get; init; }
    public string? CaptureMode { get; init; }
    public decimal AuthorizedAmount { get; init; }
    public decimal CapturedAmount { get; init; }
    public decimal RefundedAmount { get; init; }
}
