using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// Records that something now owes a financial document, and asks for it to be written.
/// </summary>
/// <remarks>
/// What every money path calls, and the only thing they have to know about documents. Announcing
/// rather than issuing keeps the composition, the numbering and the PDF entirely out of the path that
/// moved the money — so a template that throws or a storage bucket that is down cannot fail a
/// renewal, and a document that is slow to appear does not make a charge slow to settle.
/// <para>
/// Two writes, in order. First the obligation, onto the subscription itself, which is what makes the
/// document recoverable however long the delay and what freezes the terms the document will describe.
/// Then the queue entry, which only makes it prompt.
/// </para>
/// <para>
/// Nothing here ever throws. By the time it is called the money has moved and the state transition is
/// committed; a write in another database that fails costs a later document, which the repair sweep
/// finds. Throwing would turn a bookkeeping miss into an operation that looks unfinished.
/// </para>
/// </remarks>
public interface ISubscriptionFinancialDocumentAnnouncer
{
    /// <summary>
    /// Announces a settled charge, and freezes what it was for.
    /// </summary>
    /// <param name="chargeKind">
    /// What kind of charge this was. Supplied by the caller rather than parsed back out of an order
    /// id, because the caller is the one that knows — and because the period a renewal covered cannot
    /// be worked out later from a subscription that has since moved on.
    /// </param>
    /// <param name="periodKey">
    /// The billing or usage period the charge covered, for the kinds that have one. Null for the
    /// initial charge and for both settlements, whose period is the subscription's current one.
    /// </param>
    /// <param name="initiatedBy">
    /// Who asked for it, where a person did. Null for a worker, which is then named as the system
    /// rather than as whoever last touched the subscription.
    /// </param>
    Task AnnounceChargeAsync(
        SubscriptionDetail subscription,
        string paymentDetailId,
        SubscriptionChargeKind chargeKind,
        string? periodKey,
        string correlationId,
        CancellationToken cancellationToken,
        FinancialDocumentPerson? initiatedBy = null);

    /// <summary>
    /// Announces a trial that has begun, which owes a zero-total document stating its terms.
    /// </summary>
    /// <remarks>
    /// The obligation carries the trial's own dates and the plan as it was granted, so the document
    /// states what the subscriber was actually given even if they change plan the same day.
    /// </remarks>
    Task AnnounceTrialAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken,
        FinancialDocumentPerson? initiatedBy = null);

    /// <summary>
    /// Announces an opening period that activated with nothing due — a price discounted to zero, or
    /// one that was already zero — which owes a document stating what it was worth even though
    /// nothing was collected.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="AnnounceChargeAsync"/>, the figures are frozen into the obligation itself
    /// rather than left for the issuer to read off a payment: the card-setup payment behind this
    /// event carries no money and its status is never asked to reach "settled", because there is
    /// nothing for it to settle.
    /// </remarks>
    /// <param name="paymentMethodSetupPaymentId">
    /// The card-setup payment's id, used only to key the obligation for idempotency.
    /// </param>
    Task AnnounceOpeningDiscountAsync(
        SubscriptionDetail subscription,
        string paymentMethodSetupPaymentId,
        string correlationId,
        CancellationToken cancellationToken,
        FinancialDocumentPerson? initiatedBy = null);

    /// <summary>
    /// Asks for whatever a subscription already owes to be written now.
    /// </summary>
    /// <remarks>
    /// For obligations recorded by the transition itself rather than here — a change that banks credit
    /// records its own inside the compare-and-set that banks it, because that is the only write it can
    /// be atomic with. Nothing is recorded by this call; it only turns a durable obligation into a
    /// prompt one.
    /// </remarks>
    Task RequestPendingAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken);
}
