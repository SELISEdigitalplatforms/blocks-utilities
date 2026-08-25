using Payment.DomainService.Entities;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Turns something that happened to money into a document that says so.
/// </summary>
/// <remarks>
/// Every method is idempotent on its source, and that is the whole contract. Callers are money
/// paths, recovery sweeps and retried work items, and all three will call the same method for the
/// same event more than once — so "issue" means "make sure exactly one document exists for this",
/// never "add one".
/// <para>
/// Nothing here throws for an event it decides needs no document: a failed payment, a zero-amount
/// charge, a subscription that no longer exists. Those are ordinary and return null. A document
/// that <em>should</em> exist and could not be written is what throws, so the queue retries it.
/// </para>
/// </remarks>
public interface ISubscriptionFinancialDocumentIssuer
{
    /// <summary>
    /// Issues the invoice for one settled subscription charge.
    /// </summary>
    /// <remarks>
    /// Keyed on the payment rather than on the kind of charge, so one method covers the initial
    /// checkout, every renewal, a plan change, a quantity increase and a usage overage. What kind it
    /// was is read back out of the order id — the classifier the charge already carries — instead of
    /// being passed in by six call sites that could each get it wrong.
    /// </remarks>
    /// <returns>
    /// The document, or null when this payment is not a settled positive subscription charge.
    /// </returns>
    Task<SubscriptionFinancialDocument?> IssueForPaymentAsync(
        string tenantId,
        string paymentDetailId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues documents for recently settled charges that do not have one.
    /// </summary>
    /// <returns>How many documents this pass created.</returns>
    /// <remarks>
    /// The recovery path, and the reason this interface is derive-from-the-payment rather than
    /// tell-me-the-figures. A money path schedules issuing as it settles, but that scheduling write
    /// lives in another database and can be lost; this finds what nobody queued by reading the same
    /// payments and reaching the same answer.
    /// <para>
    /// Bounded by a lookback window rather than walking all history. A charge whose document was
    /// missed and never noticed inside the window is a matter for an operator, not for a sweep that
    /// re-reads years of payments every few minutes.
    /// </para>
    /// </remarks>
    Task<int> IssuePendingAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues the zero-total document stating the terms of a trial that has begun.
    /// </summary>
    /// <remarks>
    /// For every trial, card or no card. The subscriber is using entitlement they were granted, and a
    /// document that states what they were granted and when it ends is the record of that — the
    /// absence of a charge is not the absence of an agreement.
    /// </remarks>
    Task<SubscriptionFinancialDocument?> IssueTrialInvoiceAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues the credit note for a confirmed refund, linked to the invoice it adjusts.
    /// </summary>
    /// <remarks>
    /// A partial refund reverses a proportion of every figure on the original — its discounts, its
    /// tax, its lines — allocated from the original document rather than recalculated, so the credit
    /// note and the invoice reconcile to the minor unit.
    /// </remarks>
    Task<SubscriptionFinancialDocument?> IssueRefundCreditNoteAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues the credit note for a downgrade whose unused time was banked as subscription credit.
    /// </summary>
    /// <param name="creditedMinor">
    /// What was banked, as a positive figure. The document states it as value returned.
    /// </param>
    /// <remarks>
    /// Banked credit is money the subscriber has and has not spent, so it needs a document for the
    /// same reason a refund does. Credit later <em>consumed</em> by an invoice does not: that appears
    /// as a deduction on the invoice it paid for, and issuing a second document for it would count
    /// the same value twice.
    /// </remarks>
    /// <param name="changeReference">
    /// What identifies this one downgrade: its settlement reservation where there is one, otherwise
    /// the subscription version the change was applied against. Either way it is the value that makes
    /// re-issuing impossible.
    /// </param>
    /// <param name="settlement">
    /// The two-sided proration the credit came out of, when the caller has it. Shown on the document
    /// so the subscriber can see which period the returned value came from.
    /// </param>
    Task<SubscriptionFinancialDocument?> IssueDowngradeCreditNoteAsync(
        SubscriptionDetail subscription,
        string changeReference,
        long creditedMinor,
        SubscriptionSettlementBreakdown? settlement,
        string? initiatedByUserId,
        string correlationId,
        CancellationToken cancellationToken);
}
