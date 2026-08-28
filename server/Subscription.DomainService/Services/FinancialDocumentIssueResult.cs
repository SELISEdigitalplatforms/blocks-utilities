namespace Subscription.DomainService.Services;

/// <summary>
/// Why issuing a document for a payment did or did not produce one.
/// </summary>
/// <remarks>
/// This used to be <c>null</c>. Six different decisions shared it, five of them silent, and the
/// caller could not tell "there is legitimately nothing to invoice here" from "this should have
/// produced an invoice and did not". So the work handler completed its queue item either way, which
/// is the original production failure in a quieter form: the queue drains, every item succeeds, and
/// no subscriber gets an invoice.
/// <para>
/// A reason rather than a boolean because the right response differs per reason. One is a race worth
/// retrying, two are inconsistencies a person should see, and three are ordinary no-ops.
/// </para>
/// </remarks>
public enum FinancialDocumentIssueOutcome
{
    /// <summary>A document was composed and inserted by this call.</summary>
    Issued = 0,

    /// <summary>
    /// A document for this source already existed, so this call inserted nothing.
    /// </summary>
    /// <remarks>
    /// A success. The unique source index means a retry, a repair sweep and a producer all racing
    /// land on one document, and the loser is handed the winner's.
    /// </remarks>
    AlreadyExists = 1,

    /// <summary>
    /// The payment is missing, or has not settled.
    /// </summary>
    /// <remarks>
    /// Worth retrying rather than completing. The announcement exists because a charge was made, so
    /// this is usually the webhook not having landed yet; if it never settles the attempts run out
    /// and the item dead-letters, which is visible. Completing here is how a captured payment ends up
    /// with no invoice and nothing to show that anything was missed.
    /// </remarks>
    PaymentNotSettled = 2,

    /// <summary>
    /// The payment's order id does not name a subscription charge this module recognises.
    /// </summary>
    /// <remarks>
    /// Ordinary when the issuer is sweeping and meets another product's payment. <em>Not</em> ordinary
    /// when a queue item named this payment explicitly: that item was created by our own announcement,
    /// so an unrecognised order id means the two disagree, and a person should look.
    /// </remarks>
    UnknownCharge = 3,

    /// <summary>The charge names a subscription that no longer exists.</summary>
    SubscriptionMissing = 4,

    /// <summary>
    /// The charge came to nothing payable and was not a settlement, so there is nothing to describe.
    /// </summary>
    /// <remarks>
    /// A real decision, and a success: the obligation is consumed so the sweep stops rediscovering
    /// it. A settlement of zero is different and does produce a document, because the two sides
    /// cancelling out is something the subscriber is entitled to see.
    /// </remarks>
    ZeroAmount = 5
}

/// <summary>
/// The document, when there is one, and always the reason.
/// </summary>
public sealed record FinancialDocumentIssueResult(
    FinancialDocumentIssueOutcome Outcome,
    Entities.SubscriptionFinancialDocument? Document)
{
    public static FinancialDocumentIssueResult Issued(
        Entities.SubscriptionFinancialDocument document,
        bool inserted) =>
        new(
            inserted
                ? FinancialDocumentIssueOutcome.Issued
                : FinancialDocumentIssueOutcome.AlreadyExists,
            document);

    public static FinancialDocumentIssueResult Nothing(
        FinancialDocumentIssueOutcome outcome) =>
        new(outcome, null);

    /// <summary>
    /// Whether the work that named this payment can be considered done.
    /// </summary>
    /// <remarks>
    /// Deliberately not "did it produce a document". A zero charge and a foreign payment met while
    /// sweeping are both finished business with no document to show. What is <em>not</em> finished is
    /// a payment that has not settled yet, and what is not right at all is a queue item naming a
    /// payment whose charge or subscription cannot be found.
    /// </remarks>
    public bool IsSettledDecision =>
        Outcome is FinancialDocumentIssueOutcome.Issued
            or FinancialDocumentIssueOutcome.AlreadyExists
            or FinancialDocumentIssueOutcome.ZeroAmount;
}
