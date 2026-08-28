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
/// <para>
/// What a document says about the plan, price, quantities and period comes from the
/// <see cref="SubscriptionDocumentSource"/> the transition left behind, not from the subscription as
/// it stands now. The two agree in the ordinary case and disagree in exactly the case that matters:
/// a document issued after a later change would otherwise describe an old charge in terms of the new
/// plan.
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
    /// The document when there is one, and always the reason when there is not.
    /// </returns>
    /// <remarks>
    /// A typed outcome rather than a nullable document. Six decisions used to share <c>null</c>, five
    /// of them silently, so the work handler completed its queue item whichever one it was — and a
    /// queue that drains while issuing nothing is the production failure this design exists to
    /// remove, in a form that is harder to see.
    /// </remarks>
    Task<FinancialDocumentIssueResult> IssueForPaymentAsync(
        string tenantId,
        string paymentDetailId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues every document one subscription still owes, and clears the obligations.
    /// </summary>
    /// <remarks>
    /// What the work queue calls, and what the sweep calls for each subscription it finds owing. Each
    /// obligation was appended by the transition that created it, so this needs to be told nothing
    /// beyond which subscription to look at.
    /// </remarks>
    /// <returns>How many documents this call created.</returns>
    Task<int> IssueForSubscriptionAsync(
        string tenantId,
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues documents for events that do not have one, however long ago they happened.
    /// </summary>
    /// <returns>How many documents this pass created.</returns>
    /// <remarks>
    /// Three passes, because there are three ways an obligation can be discovered and none of them
    /// covers the others.
    /// <para>
    /// The first reads the obligations the transitions recorded, which is the only route by which a
    /// banked downgrade credit can be recovered at all: it charges nothing, so there is no payment
    /// left behind, and the balance it moved cannot say which change moved it.
    /// </para>
    /// <para>
    /// The second and third re-derive from settled payments and confirmed refunds, which is what
    /// covers the case where recording the obligation itself was lost. Both advance a stored
    /// high-water mark rather than looking back a fixed number of hours — a window makes recovery a
    /// function of how long the worker was away, so an outage longer than the window leaves documents
    /// that are never issued and nothing that says so.
    /// </para>
    /// </remarks>
    Task<int> IssuePendingAsync(
        string tenantId,
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
}
