using Payment.DomainService.Entities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Raises a standalone Stripe Invoice for one charge attempt — no Stripe Subscription object,
/// so Stripe never decides when the next attempt happens.
/// </summary>
/// <remarks>
/// Four calls, each raised explicitly rather than left to Stripe's own background advancement
/// (<c>auto_advance</c> is off on creation): an invoice item, the invoice itself, finalizing it,
/// and paying it. The caller decides when each step happens, which is what keeps this on the
/// same billing clock as every other renewal attempt instead of starting a second one.
/// <para>
/// <c>auto_advance</c> only withholds Stripe's own retry schedule, though — it does not stop
/// collection. A <c>charge_automatically</c> invoice is charged the moment it is finalized, so
/// finalizing can return an already-paid invoice and the pay call becomes redundant. Callers
/// must read the status a step returns rather than assuming payment happens only at
/// <see cref="PayInvoiceAsync"/>.
/// </para>
/// </remarks>
public interface IStripeInvoiceClient
{
    /// <param name="invoiceId">
    /// The draft invoice this line belongs to. Named explicitly rather than left pending for the
    /// next invoice to sweep up: recent Stripe API versions default
    /// <c>pending_invoice_items_behavior</c> to <c>exclude</c>, so a pending line is silently left
    /// off and the invoice finalizes at zero — settled, collecting nothing.
    /// </param>
    Task<StripeInvoiceCallResult> CreateInvoiceItemAsync(
        PaymentProvider provider,
        string customerId,
        string invoiceId,
        long amountMinor,
        string currencyCode,
        string description,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <param name="defaultPaymentMethodId">
    /// The card this invoice must be settled with. Named on the invoice rather than left to the
    /// customer's own default because finalizing collects immediately: without it Stripe charges
    /// whichever card the customer happens to default to, which is not necessarily the one the
    /// billing account recorded.
    /// </param>
    /// <param name="currencyCode">
    /// The subscription's currency, stated rather than inferred.
    /// </param>
    /// <remarks>
    /// Naming the currency is not optional. The invoice is created before the line item that
    /// belongs to it, so at creation there is nothing for Stripe to read a currency from and it
    /// falls back to the customer's history, then to the merchant account's own default. A line
    /// item in any other currency cannot then attach, and the invoice is left empty and unusable.
    /// <para>
    /// This fails silently in the one direction that matters: a shopper whose earlier invoices
    /// were already in the right currency inherits it and works, so the defect only appears for
    /// customers with no history — which is every genuinely new subscriber.
    /// </para>
    /// </remarks>
    Task<StripeInvoiceCallResult> CreateInvoiceAsync(
        PaymentProvider provider,
        string customerId,
        string defaultPaymentMethodId,
        string currencyCode,
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

    /// <summary>
    /// Downloads an invoice's rendered PDF.
    /// </summary>
    /// <remarks>
    /// Two calls behind one method — read the invoice for a current <c>invoice_pdf</c> link, then
    /// fetch it — because the link is the part that must not escape. It carries no authentication
    /// of its own, so anyone holding it can read the document; keeping the fetch on this side of
    /// the API means access stays the caller's own, and stays revocable.
    /// </remarks>
    Task<StripeInvoiceDocument?> DownloadInvoicePdfAsync(
        PaymentProvider provider,
        string invoiceId,
        CancellationToken cancellationToken);
}

/// <summary>
/// A rendered invoice document, held in memory because an invoice PDF is a few kilobytes and
/// streaming one through would keep a provider connection open for the caller's whole download.
/// </summary>
public sealed record StripeInvoiceDocument(
    byte[] Content,
    string ContentType,
    string? InvoiceNumber);

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
    string? SafeErrorCode = null,
    /// <summary>
    /// The invoice's <c>amount_due</c>, so a caller can check Stripe is about to collect what was
    /// asked for. Null on calls that answer with something other than an invoice.
    /// </summary>
    long? AmountMinor = null)
{
    public bool IsSuccess => Outcome == StripeInvoiceOutcome.Success;
}
