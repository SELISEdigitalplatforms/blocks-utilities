using Payment.DomainService.Entities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Raises a standalone Stripe Invoice for one charge attempt — no Stripe Subscription object,
/// so Stripe never decides when the next attempt happens.
/// </summary>
/// <remarks>
/// Four calls, each explicit rather than left to Stripe's own background advancement
/// (<c>auto_advance</c> is off on creation): an invoice item, the invoice itself, finalizing it,
/// and paying it. The caller decides when each step happens, which is what keeps this on the
/// same billing clock as every other renewal attempt instead of starting a second one.
/// </remarks>
public interface IStripeInvoiceClient
{
    Task<StripeInvoiceCallResult> CreateInvoiceItemAsync(
        PaymentProvider provider,
        string customerId,
        long amountMinor,
        string currencyCode,
        string description,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StripeInvoiceCallResult> CreateInvoiceAsync(
        PaymentProvider provider,
        string customerId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StripeInvoiceCallResult> FinalizeInvoiceAsync(
        PaymentProvider provider,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StripeInvoiceCallResult> PayInvoiceAsync(
        PaymentProvider provider,
        string invoiceId,
        string paymentMethodId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Best-effort cleanup for an invoice that will not be paid — no result to act on, since a
    /// void failing must never mask the decline that caused it.
    /// </summary>
    Task VoidInvoiceAsync(
        PaymentProvider provider,
        string invoiceId,
        CancellationToken cancellationToken);
}

public enum StripeInvoiceOutcome
{
    Success,
    Rejected,
    Unavailable,
    Timeout,
    OutcomeUnknown
}

public sealed record StripeInvoiceCallResult(
    StripeInvoiceOutcome Outcome,
    string? InvoiceOrItemId = null,
    string? Status = null,
    string? SafeErrorCode = null)
{
    public bool IsSuccess => Outcome == StripeInvoiceOutcome.Success;
}
