using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Completes a card setup once both of its independent signals are present, whichever webhook's
/// processing happens to notice second.
/// </summary>
/// <remarks>
/// A setup is "Ready" only once a successful authorization has been confirmed <em>and</em> a
/// recurring token has been received -- see <see cref="PaymentMethodSetupWebhookStateTransitionService"/>'s
/// remarks. Those two facts are recorded independently and idempotently by whichever of two
/// entirely different webhooks reports each one, in either order:
/// <list type="bullet">
/// <item>the Standard <c>AUTHORISATION</c> webhook, processed by
/// <see cref="PaymentMethodSetupWebhookStateTransitionService"/>;</item>
/// <item>the separate <c>recurring.token.created</c> webhook, processed by
/// <see cref="StoredPaymentMethodLifecycleService.ApplyTokenEventAsync"/>.</item>
/// </list>
/// Whichever of the two arrives second is the one that finds both signals present and has to
/// finish the job, so both callers share this one completion path rather than each carrying its
/// own copy of what "ready" means and how to publish it. A plain static helper rather than an
/// injected service deliberately: the two callers already inject each other's collaborators
/// (<see cref="PaymentMethodSetupWebhookStateTransitionService"/> holds a
/// <c>IStoredPaymentMethodLifecycleService</c>), and a third service sitting between them would
/// have had to be injected into one side or the other, reintroducing exactly the cycle this
/// avoids.
/// </remarks>
internal static class PaymentMethodSetupCompletion
{
    /// <summary>
    /// Publishes the setup's success outbox event and flips its status, but only once both
    /// signals are on the record. Safe to call from either signal's own handler, in either order,
    /// including after a crash and replay: <see cref="IPaymentRepository.ApplyAuthorisationAsync"/>'s
    /// own deduplication-key check is what makes a second, redundant call here a no-op rather than
    /// a double completion.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> only when this call was the one that actually completed the setup.
    /// <see langword="false"/> when a signal is still missing, or when the setup was already
    /// completed by an earlier, possibly concurrent, call.
    /// </returns>
    public static async Task<bool> TryCompleteAsync(
        IPaymentRepository payments,
        IPaymentOutboxEventFactory events,
        string tenantId,
        PaymentDetail payment,
        DateTime eventDateUtc,
        CancellationToken cancellationToken)
    {
        if (payment.SetupAuthorizationConfirmedAtUtc is null ||
            payment.SetupTokenConfirmedAtUtc is null)
        {
            // Still pending the other signal. Nothing is published, so nothing downstream --
            // including subscription activation, which watches for the outbox event this would
            // raise -- observes this setup as complete until it genuinely is.
            return false;
        }

        const string eventType = PaymentConstants.PaymentMethodSetupSucceeded;
        var outbox = events.Create(payment, eventType, PaymentStatuses.Authorized);

        // Fixed and event-independent, unlike the ordinary authorisation path's own dedup key:
        // completion can be triggered by either signal's own webhook, and only one of the (up to)
        // two calls that reach here for the same payment may actually apply it.
        outbox.DeduplicationKey = $"{payment.ItemId}:{eventType}:setup-ready";

        return await payments.ApplyAuthorisationAsync(
            tenantId,
            payment.ItemId,
            authorized: true,
            authorizedAmount: 0m,
            capturedAutomatically: false,
            payment.PspReference ?? string.Empty,
            eventDateUtc,
            instrument: null,
            outbox,
            cancellationToken);
    }
}
