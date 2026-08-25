using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Tells the queue that something now needs a financial document.
/// </summary>
/// <remarks>
/// What every money path calls, and the only thing they have to know about documents. Announcing
/// rather than issuing keeps the composition, the numbering and the PDF entirely out of the path that
/// moved the money — so a template that throws or a storage bucket that is down cannot fail a
/// renewal, and a document that is slow to appear does not make a charge slow to settle.
/// <para>
/// Nothing here ever throws. By the time it is called the money has moved and the state transition is
/// committed; a scheduling write in another database that fails costs a later document, which the
/// repair sweep finds. Throwing would turn a bookkeeping miss into an operation that looks unfinished.
/// </para>
/// </remarks>
public interface ISubscriptionFinancialDocumentAnnouncer
{
    /// <summary>Announces a settled charge, by the payment that settled it.</summary>
    Task AnnouncePaymentAsync(
        SubscriptionDetail subscription,
        string paymentDetailId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Announces that a subscription's own state now warrants a document — today, a trial that has
    /// begun.
    /// </summary>
    /// <remarks>
    /// Keyed on the subscription rather than on the trial, because the trial's terms are read back
    /// from the subscription when the work runs. That is what makes it recoverable: anything that can
    /// see the subscription can work out which document is missing.
    /// </remarks>
    Task AnnounceSubscriptionAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken);
}
