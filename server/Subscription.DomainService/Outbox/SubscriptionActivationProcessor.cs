using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
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
        ISubscriptionFinancialDocumentAnnouncer? documents = null)
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
                    Purpose = SubscriptionPaymentPurpose.InitialCharge,
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

        var applied = await _subscriptions.TryTransitionAsync(
            link.TenantId,
            link.SubscriptionId,
            new SubscriptionTransition(SubscriptionStatus.Incomplete, target)
            {
                ActivatedAtUtc = _time.GetUtcNow().UtcDateTime,
                InitialPaymentDetailId = payment.ItemId,
                DiscountPeriodsApplied = StubConsumedDiscountPeriod(subscription) ? 1 : null,

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

        await AdoptProviderCustomerAsync(subscription, payment, cancellationToken);

        if (_documents is not null)
        {
            // Announced after the transition commits, so a document is only ever promised for a
            // subscription that actually started. Both, on a trial that took a card: the charge is a
            // real charge and needs an invoice, and the trial is a real grant and needs its own
            // zero-total document stating the terms.
            await _documents.AnnounceChargeAsync(
                subscription,
                payment.ItemId,
                SubscriptionChargeKind.Initial,
                null,
                link.CorrelationId,
                cancellationToken,
                SubscriptionDocumentSourceFactory.ActorOf(payment.UserId));

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
    /// Whether this activation just paid for a prorated stub that a promotion reduced.
    /// </summary>
    /// <remarks>
    /// Read from what checkout froze, never recalculated. Whether a discount applies depends on
    /// the clock, and activation can happen long after the charge was raised: a limited promotion
    /// that lapsed in between would look inactive here while the money already taken was reduced
    /// by it, and the subscriber would get one more discounted renewal than they paid for.
    /// <para>
    /// Deliberately only a *stub*. An anniversary first period has never counted here, and making
    /// it start to would shorten every existing plan's discount by one period for reasons that
    /// have nothing to do with calendar billing.
    /// </para>
    /// </remarks>
    private static bool StubConsumedDiscountPeriod(SubscriptionDetail subscription) =>
        subscription.InitialChargeProrated && subscription.InitialChargeDiscountApplied;

    /// <summary>
    /// Records the provider's customer from the card the charge saved.
    /// </summary>
    /// <remarks>
    /// Taken from the payment rather than created up front: hosted checkout makes its own
    /// customer, so pre-creating one we cannot pin to the session would leave an orphan behind
    /// on every signup. The renewal needs this identifier, so a failure here is logged rather
    /// than swallowed — but it does not undo an activation the customer has already paid for.
    /// </remarks>
    private async Task AdoptProviderCustomerAsync(
        SubscriptionDetail subscription,
        PaymentDetail payment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.ShopperReference))
        {
            _logger.LogWarning(
                "A paid subscription has no shopper reference to find its card by; renewals " +
                "will fail until one is recorded");

            return;
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

            return;
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
    }

    private async Task<bool> AbandonAsync(
        SubscriptionPaymentLink link,
        CancellationToken cancellationToken)
    {
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
        CancellationToken cancellationToken) =>
        await _subscriptions.TryTransitionAsync(
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
