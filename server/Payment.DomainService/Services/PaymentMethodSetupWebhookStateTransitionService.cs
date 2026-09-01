using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Turns a provider's card-setup event into a settled setup record.
/// </summary>
/// <remarks>
/// The authorisation path cannot be reused for this. It proves the event against the payment's
/// amount and currency before accepting it, which is the right check for money and an impossible
/// one here — a setup has no amount, and a check against zero would prove nothing anyway. What
/// stands in for it is the flow guard below: this only ever writes to a record that was created
/// as a setup, so a stray event cannot settle a real payment through the cheaper path.
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

        // A setup's entire purpose is a durable token. A "successful" event that carries no
        // token and no shopper reference to store one under -- the shape a shopper produces by
        // declining the storePaymentMethodMode consent prompt on an otherwise-successful
        // zero-value authorisation -- is not a successful setup for this flow's purpose, whatever
        // the provider reported about the authorisation in isolation. Without this, such an event
        // left the payment reading Authorized (a terminal "succeeded" state to every caller that
        // checks PaymentStatus) while ApplyAuthorisationTokenAsync silently stored nothing --
        // a subscription that looked fully set up but had no card on file, discovered only at the
        // next renewal. Downgrading it to a refusal here instead gives the caller a clear,
        // immediate terminal failure to retry from.
        //
        // Not independently verified against a live Adyen sandbox in this environment: this
        // assumes a declined consent still arrives as an otherwise-successful authorisation
        // webhook missing the recurring token fields, which is the documented shape but was not
        // exercised live -- see the PR description's "not verified live" callout.
        if (succeeded &&
            (string.IsNullOrWhiteSpace(payload.StoredPaymentMethodToken) ||
             string.IsNullOrWhiteSpace(payload.ShopperReference)))
        {
            _logger.LogWarning(
                "Card setup reported success with no storable token -- treating as declined " +
                "PaymentHash={PaymentHash}",
                PaymentLogValue.Hash(payment.ItemId));

            succeeded = false;
        }

        if (!succeeded && IsSettled(payment))
        {
            // The session expires after it was used, or the events arrive out of order. Either
            // way the card is stored and the mandate exists; letting a later expiry overwrite
            // that would take a subscription that is already running and mark its card as never
            // collected.
            _logger.LogInformation(
                "Card setup expiry ignored Reason=already_stored PaymentHash={PaymentHash}",
                PaymentLogValue.Hash(payment.ItemId));

            return;
        }

        var eventType = succeeded
            ? PaymentConstants.PaymentMethodSetupSucceeded
            : PaymentConstants.PaymentMethodSetupFailed;
        var status = succeeded ? PaymentStatuses.Authorized : PaymentStatuses.Refused;
        var outbox = _events.Create(payment, eventType, status);
        outbox.DeduplicationKey = $"{payment.ItemId}:{eventType}:{payload.PspReference}";

        // A setup is not ready to activate until the token is durable. ApplyAuthorisationAsync
        // publishes the outbox event observed by the subscription worker, so storing the card
        // after that write leaves a race in which access is granted before renewal has a usable
        // method. The lifecycle operation is idempotent, which also makes a retry after the
        // payment-state write failed safe.
        if (succeeded)
        {
            await _storedPaymentMethods.ApplyAuthorisationTokenAsync(
                webhook,
                payment,
                cancellationToken);
        }

        // Reused verbatim, zero amount included. Nothing about this write is about money: what
        // it actually does is stamp the confirmed status and the webhook instant that the rest
        // of the system treats as proof the provider spoke, and both are exactly what a settled
        // setup needs. Never captured — there is nothing to capture — so the record stays at
        // Authorized and every reader that sums captured money passes over it.
        var applied = await _payments.ApplyAuthorisationAsync(
            webhook.TenantId,
            payment.ItemId,
            succeeded,
            0m,
            capturedAutomatically: false,
            payload.PspReference!,
            webhook.EventDateUtc,
            null,
            outbox,
            cancellationToken);

        _logger.LogInformation(
            "Card setup transition applied Applied={Applied} Succeeded={Succeeded} " +
            "PaymentHash={PaymentHash} ReasonWhenNotApplied=duplicate_or_stale_event",
            applied,
            succeeded,
            PaymentLogValue.Hash(payment.ItemId));

    }

    private static bool IsSettled(PaymentDetail payment) =>
        payment.WebhookConfirmedAtUtc is not null &&
        string.Equals(
            payment.PaymentStatus,
            PaymentStatuses.Authorized,
            StringComparison.Ordinal);
}
