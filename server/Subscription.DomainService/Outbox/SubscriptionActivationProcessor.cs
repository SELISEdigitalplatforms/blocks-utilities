using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// Turns a confirmed payment into an active subscription.
/// </summary>
/// <remarks>
/// Activation waits for the provider's webhook, never the shopper's return. A browser redirect
/// can be replayed, forged, bookmarked or simply lost when someone shuts the laptop; the
/// webhook is signed and arrives regardless. Where the two disagree, the webhook is right.
/// </remarks>
public sealed class SubscriptionActivationProcessor : ISubscriptionActivationProcessor
{
    /// <summary>Payment statuses that mean the money is ours.</summary>
    private static readonly string[] ConfirmedStatuses =
    [
        PaymentStatuses.Authorized,
        PaymentStatuses.Captured,
        PaymentStatuses.PartiallyCaptured
    ];

    /// <summary>Payment statuses that will never become confirmed.</summary>
    private static readonly string[] TerminalFailureStatuses =
    [
        PaymentStatuses.Refused,
        PaymentStatuses.Cancelled,
        PaymentStatuses.MakePaymentFailed
    ];

    private readonly ISubscriptionPaymentLinkRepository _links;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IPaymentRepository _payments;
    private readonly IStoredPaymentMethodRepository _storedMethods;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionActivationProcessor> _logger;
    private readonly TimeProvider _time;
    private readonly ISubscriptionAuditTrail? _audit;
    private readonly ICampaignRedemptionRepository? _redemptions;
    private readonly ISubscriptionRenewalService? _renewals;
    private readonly IUsageProjectionReconciler? _usageProjections;

    public SubscriptionActivationProcessor(
        ISubscriptionPaymentLinkRepository links,
        ISubscriptionRepository subscriptions,
        IBillingAccountRepository billingAccounts,
        ISubscriptionOutboxEventFactory events,
        IPaymentRepository payments,
        IStoredPaymentMethodRepository storedMethods,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionActivationProcessor> logger,
        TimeProvider? time = null,
        ISubscriptionAuditTrail? audit = null,
        ISubscriptionFinancialDocumentAnnouncer? documents = null,
        ICampaignRedemptionRepository? redemptions = null,
        ISubscriptionRenewalService? renewals = null,
        // Optional, like every collaborator threaded through this class: it publishes a read
        // model, and a caller or test unaware that model exists must keep working unchanged.
        IUsageProjectionReconciler? usageProjections = null)
    {
        _links = links;
        _subscriptions = subscriptions;
        _billingAccounts = billingAccounts;
        _events = events;
        _payments = payments;
        _storedMethods = storedMethods;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _audit = audit;
        _documents = documents;
        _redemptions = redemptions;
        _renewals = renewals;
        _usageProjections = usageProjections;
    }

    /// <summary>
    /// Optional, like the audit trail beside it: the harness and a good many tests construct this
    /// processor without one, and a missing invoice is not a reason for an activation to fail.
    /// </summary>
    private readonly ISubscriptionFinancialDocumentAnnouncer? _documents;

    public async Task<int> ProcessDueAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _links.ListDueAsync(
            tenantId,
            now,
            Math.Max(1, options.ActivationBatchSize),
            cancellationToken);

        var settled = 0;

        foreach (var link in due)
        {
            if (await SettleAsync(link, options, now, cancellationToken))
            {
                settled++;
            }
        }

        return settled;
    }

    public Task<bool> SettleLinkAsync(
        SubscriptionPaymentLink link,
        CancellationToken cancellationToken) =>
        SettleAsync(link, _options.CurrentValue, _time.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<int> RecoverStaleAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;
        var cutoff = now.AddMinutes(-Math.Max(1, options.InitialChargeGraceMinutes));

        var stale = await _subscriptions.ListStaleAsync(
            tenantId,
            SubscriptionStatus.Incomplete,
            cutoff,
            Math.Max(1, options.ActivationBatchSize),
            cancellationToken);

        var recovered = 0;

        foreach (var subscription in stale)
        {
            var link = await _links.FindBySubscriptionAsync(
                tenantId,
                subscription.ItemId,
                cancellationToken);

            if (link is not null)
            {
                continue;
            }

            // No link, so the charge either never happened or was lost before it was recorded.
            // The idempotency key is derived from the subscription, which is what makes the
            // payment findable at all — and it is uniquely indexed, so this is a point read.
            var payment = await _payments.GetByIdempotencyKeyAsync(
                tenantId,
                SubscriptionConstants.InitialChargeKeyFor(subscription.ItemId),
                cancellationToken);
            var purpose = SubscriptionPaymentPurpose.InitialCharge;

            if (payment is null)
            {
                // A subscription that owed nothing was never charged, so there is no charge to
                // find. What it may have is a card-collection session — under its own key, at the
                // attempt it had reached — and losing the link to that would expire a signup
                // whose card the provider has already stored.
                payment = await _payments.GetByIdempotencyKeyAsync(
                    tenantId,
                    SubscriptionConstants.PaymentMethodSetupKeyFor(
                        subscription.ItemId,
                        subscription.PaymentMethodSetupAttempt),
                    cancellationToken);
                purpose = SubscriptionPaymentPurpose.PaymentMethodSetup;
            }

            if (payment is null)
            {
                await ExpireAsync(subscription, cancellationToken);
                recovered++;

                continue;
            }

            await _links.TryCreateAsync(
                new SubscriptionPaymentLink
                {
                    TenantId = tenantId,
                    OrganizationId = subscription.OrganizationId,
                    SubscriptionId = subscription.ItemId,
                    PaymentDetailId = payment.ItemId,
                    OrderId = subscription.OrderId,
                    Purpose = purpose,
                    CorrelationId = subscription.CorrelationId
                },
                cancellationToken);

            _logger.LogWarning(
                "Recovered an unrecorded subscription charge TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(tenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                subscription.CorrelationId);

            recovered++;
        }

        return recovered;
    }

    private async Task<bool> SettleAsync(
        SubscriptionPaymentLink link,
        SubscriptionOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantHash"] = PaymentLogValue.Hash(link.TenantId),
            ["SubscriptionHash"] = PaymentLogValue.Hash(link.SubscriptionId),
            ["CorrelationId"] = link.CorrelationId
        });
        await AuditAsync(link, "SettlementStarted", "InProgress", null, cancellationToken);

        var payment = await _payments.GetByIdAsync(
            link.TenantId,
            link.PaymentDetailId,
            cancellationToken);

        if (payment is null)
        {
            await RescheduleAsync(link, options, now, "payment_not_found", cancellationToken);
            await AuditAsync(link, "PaymentRead", "Deferred", "payment_not_found", cancellationToken);

            return false;
        }

        if (IsConfirmed(payment))
        {
            var activated = await ActivateAsync(link, payment, cancellationToken);
            await AuditAsync(link, "ActivationApplied", activated ? "Succeeded" : "Deferred",
                activated ? null : "activation_state_conflict", cancellationToken);
            return activated;
        }

        if (TerminalFailureStatuses.Contains(payment.PaymentStatus, StringComparer.Ordinal))
        {
            var abandoned = await AbandonAsync(link, cancellationToken);
            await AuditAsync(link, "PaymentConfirmed", "Failed",
                payment.PaymentStatus, cancellationToken);
            return abandoned;
        }

        await RescheduleAsync(
            link,
            options,
            now,
            $"awaiting_confirmation:{PaymentLogValue.Label(payment.PaymentStatus)}",
            cancellationToken);
        await AuditAsync(link, "PaymentConfirmation", "Deferred",
            payment.PaymentStatus, cancellationToken);

        return false;
    }

    private Task AuditAsync(
        SubscriptionPaymentLink link,
        string stage,
        string outcome,
        string? errorCode,
        CancellationToken cancellationToken) =>
        _audit is null ? Task.CompletedTask : _audit.RecordAsync(new SubscriptionAuditEvent
        {
            TenantId = link.TenantId,
            OrganizationId = link.OrganizationId,
            SubscriptionId = link.SubscriptionId,
            OperationId = $"activation:{link.SubscriptionId}",
            CorrelationId = link.CorrelationId,
            Operation = "InitialPaymentActivation",
            Stage = stage,
            Outcome = outcome,
            Source = "Worker",
            PaymentDetailId = link.PaymentDetailId,
            ErrorCode = errorCode
        }, cancellationToken);

    /// <summary>
    /// Confirmed means both an accepting status <em>and</em> a webhook that said so.
    /// </summary>
    private static bool IsConfirmed(PaymentDetail payment) =>
        ConfirmedStatuses.Contains(payment.PaymentStatus, StringComparer.Ordinal) &&
        payment.WebhookConfirmedAtUtc is not null;

    /// <summary>Whether this link tracks a card being collected rather than money being taken.</summary>
    private static bool IsCardSetup(SubscriptionPaymentLink link) =>
        link.Purpose == SubscriptionPaymentPurpose.PaymentMethodSetup;

    private async Task<bool> ActivateAsync(
        SubscriptionPaymentLink link,
        PaymentDetail payment,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetByIdAsync(
            link.TenantId,
            link.SubscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            return await AbandonAsync(link, cancellationToken);
        }

        if (subscription.Status != SubscriptionStatus.Incomplete)
        {
            // A setup confirmed against a subscription that is already running is a card being
            // added to one — during a trial, most often — rather than one being activated. There is
            // no transition to make, but the card still has to be adopted: without this the
            // subscriber completes a Stripe session, is told it succeeded, and still has nothing on
            // file, then fails at the trial's end for want of a payment method. The early return
            // below settled the link and skipped adoption entirely.
            //
            // Left pending on failure, exactly as the activation path does, so the sweep tries
            // again rather than losing a card the subscriber has already entered.
            if (IsCardSetup(link) &&
                !await AdoptProviderCustomerAsync(subscription, payment, cancellationToken))
            {
                return false;
            }

            // The card just adopted is what an Unpaid subscription was missing, so this is the
            // moment recovery becomes possible — not a moment later, and not left for a sweep
            // that (correctly) never looks at an Unpaid subscription on its own. The link settles
            // either way: it is a record that the card was stored, and it stored successfully
            // whether or not the charge that follows is accepted.
            if (IsCardSetup(link) &&
                subscription.Status == SubscriptionStatus.Unpaid &&
                _renewals is not null)
            {
                await _renewals.RecoverAsync(subscription, cancellationToken);
            }

            // Already carried across by an earlier pass. Settle the link so it stops coming back.
            return await _links.TrySettleAsync(
                link.TenantId,
                link.ItemId,
                SubscriptionPaymentLinkState.Applied,
                cancellationToken);
        }

        var target = subscription.Trial is null
            ? SubscriptionStatus.Active
            : SubscriptionStatus.Trialing;

        var eventType = target == SubscriptionStatus.Trialing
            ? SubscriptionConstants.SubscriptionTrialStarted
            : SubscriptionConstants.SubscriptionActivated;

        // A setup confirmation is only useful when the exact stored card has also been wired to
        // the billing account. Do this before granting access; unlike a paid checkout there is no
        // captured money whose entitlement must be honoured while a repair is retried.
        if (IsCardSetup(link) &&
            !await AdoptProviderCustomerAsync(subscription, payment, cancellationToken))
        {
            return false;
        }

        var applied = await _subscriptions.TryTransitionAsync(
            link.TenantId,
            link.SubscriptionId,
            new SubscriptionTransition(SubscriptionStatus.Incomplete, target)
            {
                ActivatedAtUtc = _time.GetUtcNow().UtcDateTime,
                // Only when this record is a charge. A card setup produces a payment row so the
                // provider machinery has something to hang a session off, but it holds no money —
                // naming it as the initial payment would put a zero-value entry where every
                // reader expects the opening charge, and invoice history would go looking for a
                // document that was never issued.
                InitialPaymentDetailId = IsCardSetup(link) ? null : payment.ItemId,
                DiscountPeriodsApplied =
                    SubscriptionDiscountPeriodAccounting.OpeningChargeSpentPeriod(subscription)
                        ? 1
                        : null,
                // The opening charge included the year on a price that collects it here, and this
                // is the transition that says that charge was confirmed. Marking it any earlier
                // would report an unpaid checkout as settled; any later leaves the boundary unable
                // to tell a paid year from one still owed.
                MarkPendingAnnualPeriodPrepaid =
                    subscription.PendingAnnualPeriod is { CollectedWithCheckout: true },

                Event = _events.Create(
                    subscription,
                    eventType,
                    link.CorrelationId,
                    payment.ItemId)
            },
            cancellationToken);

        if (!applied)
        {
            // Another worker got there first. Its transition is as good as this one's.
            return false;
        }

        if (_redemptions is not null &&
            subscription.Discount is { Campaign.Kind: not CampaignKind.Standard } discount)
        {
            // After the transition commits, never before: a campaign is redeemed because this
            // subscription actually activated, and marking it redeemed on a transition that then
            // failed to apply would grant the permanent half of a redemption for an activation
            // that never happened. Idempotent against a duplicate delivery of this same event --
            // the repository itself guarantees that, not a check made here.
            await _redemptions.TryMarkRedeemedAsync(
                subscription.TenantId,
                discount.DiscountId!,
                subscription.ItemId,
                _time.GetUtcNow().UtcDateTime,
                cancellationToken);
        }

        if (_usageProjections is not null)
        {
            // After the transition commits, for the same reason the redemption above is: this
            // publishes what the subscription now is, and publishing it against a transition that
            // then failed to apply would advertise meters and allowances for a subscription nobody
            // activated.
            //
            // Re-reads the subscription rather than projecting the copy in hand, which is still the
            // pre-transition document: its status says Incomplete, and a projection carrying that
            // would tell a reader the allowance is not yet granted at the exact moment it was.
            //
            // This is what lets a consumer discover a subscription's meters and allowances before
            // any usage exists — the difference, to a reader that cannot see the plan, between "no
            // usage yet" and "no such meter". It creates zero-usage documents and never overwrites a
            // balance, so it is safe if usage somehow arrived first.
            await _usageProjections.RefreshSubscriptionAsync(
                link.TenantId,
                link.SubscriptionId,
                link.CorrelationId,
                cancellationToken);
        }

        if (!IsCardSetup(link))
        {
            await AdoptProviderCustomerAsync(subscription, payment, cancellationToken);
        }

        if (_documents is not null)
        {
            // Announced after the transition commits, so a document is only ever promised for a
            // subscription that actually started.
            //
            // Never a charge for a card setup. The payment row behind a setup exists so the provider
            // machinery has something to hang a session off and holds no money at all; invoicing it
            // as a charge would issue a document describing money that was never taken.
            if (!IsCardSetup(link))
            {
                await _documents.AnnounceChargeAsync(
                    subscription,
                    payment.ItemId,
                    SubscriptionChargeKind.Initial,
                    null,
                    link.CorrelationId,
                    cancellationToken,
                    SubscriptionDocumentSourceFactory.ActorOf(payment.UserId));
            }
            else if (target == SubscriptionStatus.Active)
            {
                // A card setup landing directly on Active, rather than on Trialing, is an opening
                // period that owed nothing today — a price discounted to zero, or one that was
                // already zero — not a trial. It still deserves a document stating what it was
                // worth, the same reasoning a trial invoice already applies to a period nobody paid
                // for; see AnnounceOpeningDiscountAsync for why its figures are frozen here rather
                // than read off this payment.
                await _documents.AnnounceOpeningDiscountAsync(
                    subscription,
                    payment.ItemId,
                    link.CorrelationId,
                    cancellationToken,
                    SubscriptionDocumentSourceFactory.ActorOf(payment.UserId));
            }

            if (target == SubscriptionStatus.Trialing)
            {
                await _documents.AnnounceTrialAsync(
                    subscription,
                    link.CorrelationId,
                    cancellationToken);
            }
        }

        _logger.LogInformation(
            "Subscription activated Status={Status} PaymentHash={PaymentHash}",
            PaymentLogValue.Label(target.ToString()),
            PaymentLogValue.Hash(payment.ItemId));

        return await _links.TrySettleAsync(
            link.TenantId,
            link.ItemId,
            SubscriptionPaymentLinkState.Applied,
            cancellationToken);
    }

    /// <summary>
    /// Records the provider's customer from the card the charge saved.
    /// </summary>
    /// <remarks>
    /// Taken from the payment rather than created up front: hosted checkout makes its own
    /// customer, so pre-creating one we cannot pin to the session would leave an orphan behind
    /// on every signup. The renewal needs this identifier, so a failure here is logged rather
    /// than swallowed — but it does not undo an activation the customer has already paid for.
    /// </remarks>
    private async Task<bool> AdoptProviderCustomerAsync(
        SubscriptionDetail subscription,
        PaymentDetail payment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.ShopperReference))
        {
            _logger.LogWarning(
                "A paid subscription has no shopper reference to find its card by; renewals " +
                "will fail until one is recorded");

            return false;
        }

        // Found by the reference the card was saved under, not by a link from the payment.
        // Hosted checkout never writes StoredPaymentMethodPublicId — only a charge already made
        // from a stored card does — so reading it here meant every subscription reached its
        // first renewal with no card, and failed closed a whole billing period after the
        // mistake was made.
        var methods = await _storedMethods.ListActiveAsync(
            subscription.TenantId,
            [new StoredPaymentMethodLookupScope(
                payment.ShopperReference,
                payment.OrganizationId)],
            cancellationToken);

        // The card this charge saved, newest first: a shopper who paid twice has more than one,
        // and the one they just used is the one a renewal should charge.
        var method = (methods ?? [])
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefault(candidate =>
                candidate.ProviderPayerReference is { Length: > 0 });

        if (method?.ProviderPayerReference is not { Length: > 0 } customerId)
        {
            _logger.LogWarning(
                "No provider customer recorded for a subscription; renewals will need one");

            return false;
        }

        var outcome = await _billingAccounts.TrySetProviderCustomerAsync(
            subscription.TenantId,
            subscription.BillingAccountId,
            customerId,
            method.ItemId,
            // The scope that took the money, which is what later charges must resolve the
            // provider under. Organizations subscribe; the tenant is the merchant.
            payment.OrganizationId,
            cancellationToken);

        // Never discarded. Whether the card a renewal will present actually got recorded is the
        // one thing this method exists to decide, and the silence here is what let a billing
        // account sit for a month pointing at a removed card while everything else read healthy.
        switch (outcome)
        {
            case SetProviderCustomerOutcome.Repointed:
                _logger.LogWarning(
                    "A subscription's billing account moved to a different provider customer; " +
                    "cards saved against the previous one are no longer reachable " +
                    "TenantHash={TenantHash} SubscriptionHash={SubscriptionHash}",
                    PaymentLogValue.Hash(subscription.TenantId),
                    PaymentLogValue.Hash(subscription.ItemId));
                break;

            case SetProviderCustomerOutcome.AccountMissing:
                _logger.LogError(
                    "A paid subscription has no billing account to record its card against; " +
                    "renewals will find no payment method " +
                    "TenantHash={TenantHash} SubscriptionHash={SubscriptionHash}",
                    PaymentLogValue.Hash(subscription.TenantId),
                    PaymentLogValue.Hash(subscription.ItemId));
                break;
        }

        return outcome != SetProviderCustomerOutcome.AccountMissing;
    }

    /// <summary>
    /// Gives up on this attempt, and on the subscription behind it unless another attempt is
    /// reasonable.
    /// </summary>
    /// <remarks>
    /// A declined charge ends the subscription: the money was refused, and leaving it Incomplete
    /// would hold the organization's one live slot open indefinitely for a customer whose card
    /// said no.
    /// <para>
    /// A card setup that failed or expired is a different thing entirely. Nothing was refused,
    /// because nothing was asked for; the usual cause is a page left open past the session's
    /// life. The subscription stays Incomplete so the next request can open a fresh session, and
    /// the recovery sweep still expires it if nobody comes back — that sweep works from the
    /// subscription's age rather than from this link.
    /// </para>
    /// </remarks>
    private async Task<bool> AbandonAsync(
        SubscriptionPaymentLink link,
        CancellationToken cancellationToken)
    {
        if (IsCardSetup(link))
        {
            _logger.LogInformation(
                "Subscription card setup abandoned; the subscription stays open for another attempt");

            return await _links.TrySettleAsync(
                link.TenantId,
                link.ItemId,
                SubscriptionPaymentLinkState.Abandoned,
                cancellationToken);
        }

        var subscription = await _subscriptions.GetByIdAsync(
            link.TenantId,
            link.SubscriptionId,
            cancellationToken);

        if (subscription is not null)
        {
            await ExpireAsync(subscription, cancellationToken);
        }

        _logger.LogInformation("Subscription activation abandoned after a failed charge");

        return await _links.TrySettleAsync(
            link.TenantId,
            link.ItemId,
            SubscriptionPaymentLinkState.Abandoned,
            cancellationToken);
    }

    private async Task ExpireAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken)
    {
        var applied = await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            new SubscriptionTransition(
                SubscriptionStatus.Incomplete,
                SubscriptionStatus.IncompleteExpired)
            {
                EndedAtUtc = _time.GetUtcNow().UtcDateTime,
                ClearNextFeeBillingAt = true,
                Event = _events.Create(
                    subscription,
                    SubscriptionConstants.SubscriptionActivationFailed,
                    subscription.CorrelationId)
            },
            cancellationToken);

        // The transition's own guard is what makes this safe to call unconditionally: it only
        // ever moves a subscription out of Incomplete, and Incomplete is exactly "never
        // activated" -- there is no path from here to a subscription that already redeemed its
        // campaign. TryReleaseAsync's own guard against an already-Redeemed row is defence in
        // depth on top of that, not the only thing preventing it.
        if (applied &&
            _redemptions is not null &&
            subscription.Discount is { Campaign.Kind: not CampaignKind.Standard } discount)
        {
            await _redemptions.TryReleaseAsync(
                subscription.TenantId,
                discount.DiscountId!,
                subscription.ItemId,
                _time.GetUtcNow().UtcDateTime,
                cancellationToken);
        }
    }

    private async Task RescheduleAsync(
        SubscriptionPaymentLink link,
        SubscriptionOptions options,
        DateTime now,
        string reason,
        CancellationToken cancellationToken)
    {
        var attempt = link.AttemptCount + 1;

        if (attempt >= Math.Max(1, options.ActivationMaxAttempts))
        {
            await AbandonAsync(link, cancellationToken);

            return;
        }

        // Backs off linearly rather than exponentially: this is waiting on a webhook that
        // usually lands in seconds, so a doubling delay would leave a paid subscription
        // inactive for far longer than the payment took.
        var delay = TimeSpan.FromSeconds(
            Math.Max(1, options.ActivationRetrySeconds) * attempt);

        await _links.RescheduleAsync(
            link.TenantId,
            link.ItemId,
            attempt,
            now.Add(delay),
            reason,
            cancellationToken);
    }
}
