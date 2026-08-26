using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Responses;

/// <summary>
/// What a deliberate resend was queued as.
/// </summary>
/// <remarks>
/// Reports the intent, not the delivery. The mail is published by the same work type every other
/// document's is, so by the time this returns the resend is durable and not yet sent — which is the
/// honest thing to say about it, and the reason the response names the recipient rather than claiming
/// an outcome.
/// </remarks>
public sealed class SubscriptionFinancialDocumentResendResponse
{
    public string DocumentId { get; init; } = string.Empty;

    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>
    /// Where it will go: the billing contact snapshotted on the document, not the profile's current
    /// one. Editing the profile does not redirect an issued document's mail.
    /// </summary>
    public string Recipient { get; init; } = string.Empty;

    /// <summary>
    /// The identity this document's mail carries, unchanged by resending.
    /// </summary>
    /// <remarks>
    /// Worth returning because it is what a mail consumer would deduplicate on, and what a support
    /// conversation about a duplicate would quote.
    /// </remarks>
    public string MessageId { get; init; } = string.Empty;

    /// <summary>
    /// Which resend this is, counting from one.
    /// </summary>
    /// <remarks>
    /// Part of the identity of the queued work rather than a statistic. The queue admits one item per
    /// occurrence and enforces that over finished items too, so each resend has to be its own
    /// occurrence or it is refused as a duplicate of the delivery that already ran.
    /// </remarks>
    public int ResendGeneration { get; init; }

    /// <summary>
    /// What this request actually did.
    /// </summary>
    /// <remarks>
    /// Three answers rather than a success flag, because the mail is going to be sent in all three and
    /// that is not the useful part. <c>JoinedPending</c> is what a double click gets: the request
    /// succeeded, nothing new was scheduled, and no second email will arrive.
    /// </remarks>
    public FinancialDocumentResendStatus Status { get; init; }
}
