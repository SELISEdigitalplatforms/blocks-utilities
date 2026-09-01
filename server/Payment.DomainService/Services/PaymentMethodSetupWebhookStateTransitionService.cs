using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Turns a provider's card-setup events into a settled setup record.
/// </summary>
/// <remarks>
/// The authorisation path cannot be reused for this. It proves the event against the payment's
/// amount and currency before accepting it, which is the right check for money and an impossible
/// one here — a setup has no amount, and a check against zero would prove nothing anyway. What
/// stands in for it is the flow guard below: this only ever writes to a record that was created
/// as a setup, so a stray event cannot settle a real payment through the cheaper path.
/// <para>
/// A setup is modelled as two independent signals rather than one event's outcome, because Adyen
/// creates a recurring token asynchronously and reports it on a separate
/// <c>recurring.token.created</c> webhook that can arrive before <em>or</em> after the Standard
/// <c>AUTHORISATION</c> webhook this type otherwise handles (see
/// https://docs.adyen.com/online-payments/tokenization/create-tokens). Both signals are recorded
/// independently and idempotently — see <see cref="Entities.PaymentDetail.SetupAuthorizationConfirmedAtUtc"/>
/// and <see cref="Entities.PaymentDetail.SetupTokenConfirmedAtUtc"/> — and the setup only becomes
/// Ready, publishing its success outbox event, once both are present:
/// <list type="bullet">
/// <item><b>Authorization pending + Token pending</b> — the initial state.</item>
/// <item><b>Authorization succeeded + Token pending</b> — this type recorded a successful
/// AUTHORISATION and found no token signal yet; nothing is published.</item>
/// <item><b>Token received + Authorization pending</b> — the token webhook arrived first; see
/// <see cref="StoredPaymentMethodLifecycleService.ApplyTokenEventAsync"/>.</item>
/// <item><b>Ready</b> — both signals present, in either order; <see cref="PaymentMethodSetupCompletion"/>
/// completes it exactly once.</item>
/// <item><b>Failed</b> — an explicit negative signal: the provider reported the authorization
/// itself as refused, failed or cancelled. Never inferred from a successful event's silence about
/// a token, which is a corrected defect from an earlier round — see PR #393.</item>
/// <item><b>Expired</b> — a setup left pending one signal past
/// <see cref="Utilities.PaymentOptions.PaymentMethodSetupTimeoutSeconds"/>, so it stops blocking
/// its idempotency key forever. Applied by the separate recovery sweep in
/// <see cref="PaymentMethodSetupExpiryProcessor"/>, not by this type: that sweep's
/// compare-and-set re-checks both the status and that a signal is still missing atomically in the
/// same write, so a completion or decline landing concurrently always wins over the expiry — see
/// <see cref="Repositories.IPaymentRepository.TryExpireSetupAsync"/> and PR #393 review
/// (Finding 1).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class PaymentMethodSetupWebhookStateTransitionService :
    IPaymentMethodSetupWebhookStateTransitionService
{
    private readonly IPaymentRepository _payments;
    private readonly IStoredPaymentMethodLifecycleService _storedPaymentMethods;
    private readonly IPaymentOutboxEventFactory _events;
    private readonly ILogger<PaymentMethodSetupWebhookStateTransitionService> _logger;

    public PaymentMethodSetupWebhookStateTransitionService(
        IPaymentRepository payments,
        IStoredPaymentMethodLifecycleService storedPaymentMethods,
        IPaymentOutboxEventFactory events,
        ILogger<PaymentMethodSetupWebhookStateTransitionService> logger)
    {
        _payments = payments;
        _storedPaymentMethods = storedPaymentMethods;
        _events = events;
        _logger = logger;
    }

    public async Task ApplyAsync(PaymentWebhookInbox webhook, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        var payload = webhook.NormalizedPayload;

        if (string.IsNullOrWhiteSpace(payload.PaymentDetailId) ||
            string.IsNullOrWhiteSpace(payload.PspReference) ||
            !payload.Success.HasValue)
        {
            // A session ending is reported by one event name whatever the session was for, and
            // an unroutable one belongs to a checkout this service has never been able to act
            // on. Retrying it to the dead-letter queue would turn a long-standing no-op into a
            // stream of alerts about abandoned baskets.
            if (webhook.Intent == WebhookIntent.Cancelled)
            {
                _logger.LogInformation(
                    "Session-ended event ignored Reason=incomplete_normalized_event");

                return;
            }

            _logger.LogError(
                "Card setup transition rejected Reason=incomplete_normalized_event " +
                "HasPaymentId={HasPaymentId} HasPspReference={HasPspReference} HasSuccess={HasSuccess}",
                !string.IsNullOrWhiteSpace(payload.PaymentDetailId),
                !string.IsNullOrWhiteSpace(payload.PspReference),
                payload.Success.HasValue);

            throw new InvalidOperationException("Incomplete normalized card setup event.");
        }

        var payment = await _payments.GetByIdAsync(
            webhook.TenantId,
            payload.PaymentDetailId,
            cancellationToken);

        if (payment is null)
        {
            if (webhook.Intent == WebhookIntent.Cancelled)
            {
                _logger.LogInformation(
                    "Session-ended event ignored Reason=payment_not_found");

                return;
            }

            _logger.LogError("Card setup transition rejected Reason=payment_not_found");

            throw new InvalidOperationException("Payment reference was not found.");
        }

        if (!string.Equals(
                payment.PaymentFlow,
                PaymentFlows.PaymentMethodSetup,
                StringComparison.Ordinal))
        {
            // An expiring or cancelled *payment* session reaches here too, because a provider
            // uses one event name for both. It has always been a no-op for those, and making it
            // one now would be a change to how abandoned checkouts behave, decided somewhere
            // other than a card-setup handler.
            _logger.LogInformation(
                "Card setup transition skipped Reason=not_a_setup_record PaymentFlow={PaymentFlow}",
                PaymentLogValue.Label(payment.PaymentFlow));

            return;
        }

        var succeeded = payload.Success.Value;

        if (!succeeded)
        {
            // An explicit negative signal from the provider -- a genuine decline, failure or
            // cancellation of the authorization itself -- is authoritative on its own and needs
            // no token signal to act on. This is deliberately narrower than what this branch used
            // to do: it no longer infers a decline merely because this event's own payload
            // carries no token, which conflated "the shopper said no" with "the token simply has
            // not arrived on this event yet" -- Adyen's recurring token is created asynchronously
            // and reported on a separate recurring.token.created webhook that can arrive before
            // or after this one (see https://docs.adyen.com/online-payments/tokenization/create-tokens
            // and the type's own remarks). Treating silence as decline there produced false
            // rejections of a setup whose token event was simply still in flight -- see PR #393.
            if (IsSettled(payment))
            {
                // The session expires after it was used, or events arrive out of order. Either
                // way the card is already stored and the mandate exists; letting a later expiry
                // or decline overwrite that would take a subscription that is already running and
                // mark its card as never collected.
                _logger.LogInformation(
                    "Card setup decline ignored Reason=already_stored PaymentHash={PaymentHash}",
                    PaymentLogValue.Hash(payment.ItemId));

                return;
            }

            await FinalizeFailureAsync(webhook, payment, payload.PspReference!, cancellationToken);
            return;
        }

        // A successful authorization is only one of the two independent signals a setup needs --
        // see this type's own remarks on the two-signal state machine. Recorded as a fact of its
        // own, idempotently, rather than treated as the whole outcome: TryCompleteIfReadyAsync
        // below is what decides whether the setup is actually Ready.
        await _payments.TryRecordSetupAuthorizationConfirmedAsync(
            webhook.TenantId,
            payment.ItemId,
            webhook.EventDateUtc,
            payload.PspReference!,
            cancellationToken);

        // Not independently verified against a live Adyen sandbox in this environment: whether
        // Adyen ever reports the token inline on the same AUTHORISATION event, as opposed to only
        // ever on the separate recurring.token.created webhook, is not confirmed either way here.
        // Treated as an opportunistic fast path when present on this event, never as a
        // requirement -- the separate token webhook, handled by
        // IStoredPaymentMethodLifecycleService.ApplyTokenEventAsync, is the documented path and
        // works whether or not this one ever fires.
        if (!string.IsNullOrWhiteSpace(payload.StoredPaymentMethodToken) &&
            !string.IsNullOrWhiteSpace(payload.ShopperReference))
        {
            await _payments.TryRecordSetupTokenConfirmedAsync(
                webhook.TenantId,
                payment.ItemId,
                webhook.EventDateUtc,
                cancellationToken);

            await _storedPaymentMethods.ApplyAuthorisationTokenAsync(
                webhook,
                payment,
                cancellationToken);
        }

        await TryCompleteIfReadyAsync(webhook, payment, cancellationToken);
    }

    /// <summary>
    /// Completes the setup once both signals -- authorization and token -- are on the record, in
    /// whichever order they arrived. Re-reads the payment rather than trusting the in-memory copy
    /// this method was handed: the other signal may have been recorded moments ago by an entirely
    /// different webhook (recurring.token.created, handled by
    /// <see cref="IStoredPaymentMethodLifecycleService.ApplyTokenEventAsync"/>) that this call
    /// never itself observed.
    /// </summary>
    private async Task TryCompleteIfReadyAsync(
        PaymentWebhookInbox webhook,
        PaymentDetail payment,
        CancellationToken cancellationToken)
    {
        var current = await _payments.GetByIdAsync(
            webhook.TenantId,
            payment.ItemId,
            cancellationToken);

        if (current is null)
        {
            return;
        }

        var completed = await PaymentMethodSetupCompletion.TryCompleteAsync(
            _payments,
            _events,
            webhook.TenantId,
            current,
            webhook.EventDateUtc,
            cancellationToken);

        _logger.LogInformation(
            "Card setup readiness evaluated Completed={Completed} " +
            "HasAuthorizationSignal={HasAuthorizationSignal} HasTokenSignal={HasTokenSignal} " +
            "PaymentHash={PaymentHash}",
            completed,
            current.SetupAuthorizationConfirmedAtUtc is not null,
            current.SetupTokenConfirmedAtUtc is not null,
            PaymentLogValue.Hash(payment.ItemId));
    }

    private async Task FinalizeFailureAsync(
        PaymentWebhookInbox webhook,
        PaymentDetail payment,
        string pspReference,
        CancellationToken cancellationToken)
    {
        var outbox = _events.Create(
            payment,
            PaymentConstants.PaymentMethodSetupFailed,
            PaymentStatuses.Refused);
        outbox.DeduplicationKey =
            $"{payment.ItemId}:{PaymentConstants.PaymentMethodSetupFailed}:{pspReference}";

        // Reused verbatim, zero amount included. Nothing about this write is about money: what
        // it actually does is stamp the confirmed status and the webhook instant that the rest
        // of the system treats as proof the provider spoke, and both are exactly what a settled
        // setup needs.
        var applied = await _payments.ApplyAuthorisationAsync(
            webhook.TenantId,
            payment.ItemId,
            authorized: false,
            authorizedAmount: 0m,
            capturedAutomatically: false,
            pspReference,
            webhook.EventDateUtc,
            null,
            outbox,
            cancellationToken);

        _logger.LogInformation(
            "Card setup transition applied Applied={Applied} Succeeded=False " +
            "PaymentHash={PaymentHash} ReasonWhenNotApplied=duplicate_or_stale_event",
            applied,
            PaymentLogValue.Hash(payment.ItemId));
    }

    private static bool IsSettled(PaymentDetail payment) =>
        payment.WebhookConfirmedAtUtc is not null &&
        string.Equals(
            payment.PaymentStatus,
            PaymentStatuses.Authorized,
            StringComparison.Ordinal);
}
