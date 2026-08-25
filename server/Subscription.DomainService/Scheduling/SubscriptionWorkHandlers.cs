using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// The handlers, each delegating to the processor that already owns its rules.
/// </summary>
/// <remarks>
/// Deliberately thin. The processors re-read the tenant's own state, decide what is still due, and
/// derive their provider idempotency keys from persisted identity — a renewal from its period and
/// attempt, a settlement from its reservation. Reimplementing any of that here would give the same
/// money two sets of rules, and the scheduler is meant to change <em>when</em> work runs, not what
/// running it means.
/// <para>
/// That is also what makes a retried item safe: the second attempt walks the same code that
/// recognizes the first attempt's charge, rather than raising a new one.
/// </para>
/// </remarks>
public sealed class ActivationSettlementWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionActivationProcessor _activation;

    public ActivationSettlementWorkHandler(ISubscriptionActivationProcessor activation) =>
        _activation = activation;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.ActivationSettlement;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _activation.ProcessDueAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

/// <summary>
/// Recovers a first charge that was raised and never recorded, or gives up on one never paid.
/// </summary>
/// <remarks>
/// An item naming a subscription checks that one first. Most will find a subscription that paid
/// normally minutes after the item was scheduled, and can finish without touching anything else —
/// which is the point of scheduling per subscription rather than per tenant.
/// <para>
/// Where recovery <em>is</em> needed the tenant pass runs, because deciding what became of a charge
/// belongs to the activation processor: it compares links against payments by derived idempotency
/// key, and doing that here would be a second implementation of the one rule that keeps a shopper
/// from paying twice.
/// </para>
/// </remarks>
public sealed class ActivationRecoveryWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionActivationProcessor _activation;
    private readonly ISubscriptionRepository _subscriptions;

    public ActivationRecoveryWorkHandler(
        ISubscriptionActivationProcessor activation,
        ISubscriptionRepository subscriptions)
    {
        _activation = activation;
        _subscriptions = subscriptions;
    }

    public SubscriptionWorkType WorkType => SubscriptionWorkType.ActivationRecovery;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(work.AggregateId))
        {
            var subscription = await _subscriptions.GetByIdAsync(
                work.TenantId,
                work.AggregateId,
                cancellationToken);

            if (subscription is null)
            {
                return SubscriptionWorkOutcome.Permanent(
                    "subscription_not_found",
                    "The subscription this work names no longer exists.");
            }

            if (subscription.Status != SubscriptionStatus.Incomplete)
            {
                // Paid, or already expired. The ordinary outcome: this item was scheduled when the
                // checkout was created and the shopper finished before it came due.
                return SubscriptionWorkOutcome.Completed();
            }
        }

        await _activation.RecoverStaleAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

public sealed class SettlementReservationRecoveryWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionSettlementReservationProcessor _reservations;

    public SettlementReservationRecoveryWorkHandler(
        ISubscriptionSettlementReservationProcessor reservations) =>
        _reservations = reservations;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.SettlementReservationRecovery;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _reservations.RecoverStaleAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

/// <summary>
/// Renews either one named subscription or every due subscription in a tenant.
/// </summary>
/// <remarks>
/// Both shapes exist on purpose. Work scheduled where the state changed names the subscription it
/// is about, which is the point of the queue — a tenant with one renewal due costs one claim rather
/// than a pass over all its subscriptions. Work scheduled by the repair sweep names no aggregate,
/// because the sweep's job is precisely to find what nobody scheduled.
/// </remarks>
public sealed class RenewalWorkHandler : ISubscriptionWorkHandler
{
    private static readonly SubscriptionStatus[] RenewableStatuses =
    [
        SubscriptionStatus.Active,
        SubscriptionStatus.PastDue
    ];

    private readonly ISubscriptionRenewalProcessor _renewals;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionRenewalService _renewalService;
    private readonly TimeProvider _time;

    public RenewalWorkHandler(
        ISubscriptionRenewalProcessor renewals,
        ISubscriptionRepository subscriptions,
        ISubscriptionRenewalService renewalService,
        TimeProvider? time = null)
    {
        _renewals = renewals;
        _subscriptions = subscriptions;
        _renewalService = renewalService;
        _time = time ?? TimeProvider.System;
    }

    public SubscriptionWorkType WorkType => SubscriptionWorkType.Renewal;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(work.AggregateId))
        {
            await _renewals.ProcessDueAsync(work.TenantId, cancellationToken);

            return SubscriptionWorkOutcome.Completed();
        }

        // Read from the tenant's own database, never trusted from the scheduling document. The two
        // share no transaction, so this item may be describing a subscription that has since been
        // cancelled, changed plan, or already renewed by the sweep.
        var subscription = await _subscriptions.GetByIdAsync(
            work.TenantId,
            work.AggregateId,
            cancellationToken);

        if (subscription is null)
        {
            // Nothing to renew and nothing a retry can change.
            return SubscriptionWorkOutcome.Permanent(
                "subscription_not_found",
                "The subscription this work names no longer exists.");
        }

        if (!RenewableStatuses.Contains(subscription.Status))
        {
            // Cancelled, unpaid, or still incomplete. Not an error: the state moved on after this
            // was scheduled, which is exactly what re-reading is for.
            return SubscriptionWorkOutcome.Completed();
        }

        if (subscription.NextFeeBillingAtUtc is not { } dueAt ||
            dueAt > _time.GetUtcNow().UtcDateTime)
        {
            // Already renewed, or not due yet. Completing rather than retrying, because the next
            // occurrence is scheduled by whoever renews it.
            return SubscriptionWorkOutcome.Completed();
        }

        await _renewalService.RenewAsync(subscription, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

public sealed class UsagePeriodClosureWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionUsageRatingProcessor _usageRating;

    public UsagePeriodClosureWorkHandler(ISubscriptionUsageRatingProcessor usageRating) =>
        _usageRating = usageRating;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.UsagePeriodClosure;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _usageRating.CloseDuePeriodsAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

public sealed class UsageInvoiceChargeWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionUsageRatingProcessor _usageRating;

    public UsageInvoiceChargeWorkHandler(ISubscriptionUsageRatingProcessor usageRating) =>
        _usageRating = usageRating;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.UsageInvoiceCharge;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _usageRating.ChargeDueInvoicesAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

/// <summary>
/// Issues the financial document for one settled charge, or recovers the ones nobody queued.
/// </summary>
/// <remarks>
/// Both shapes, for the reason the renewal handler has both. Work scheduled where the money settled
/// names the payment it is about, which is the point of the queue. Work scheduled by the repair sweep
/// names nothing, because its job is precisely to find what no producer announced.
/// </remarks>
public sealed class FinancialDocumentIssueWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionFinancialDocumentIssuer _issuer;
    private readonly ISubscriptionRepository _subscriptions;

    public FinancialDocumentIssueWorkHandler(
        ISubscriptionFinancialDocumentIssuer issuer,
        ISubscriptionRepository subscriptions)
    {
        _issuer = issuer;
        _subscriptions = subscriptions;
    }

    public SubscriptionWorkType WorkType => SubscriptionWorkType.FinancialDocumentIssue;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (string.IsNullOrWhiteSpace(work.AggregateId))
        {
            await _issuer.IssuePendingAsync(work.TenantId, work.CorrelationId, cancellationToken);

            return SubscriptionWorkOutcome.Completed();
        }

        // The work key says which kind of id the aggregate is. Read from the key rather than guessed
        // from the shape of the id, because a subscription id and a payment id are both GUIDs and
        // guessing wrong would look up the right id in the wrong collection and find nothing.
        if (work.WorkKey.StartsWith(
                SubscriptionFinancialDocumentAnnouncer.SubscriptionWorkKeyPrefix,
                StringComparison.Ordinal))
        {
            var subscription = await _subscriptions.GetByIdAsync(
                work.TenantId,
                work.AggregateId,
                cancellationToken);

            if (subscription is null)
            {
                return SubscriptionWorkOutcome.Permanent(
                    "subscription_not_found",
                    "The subscription this work names no longer exists.");
            }

            await _issuer.IssueTrialInvoiceAsync(
                subscription,
                work.CorrelationId,
                cancellationToken);

            return SubscriptionWorkOutcome.Completed();
        }

        // Completed whatever the answer. A payment that turned out not to need a document — a
        // declined attempt, a foreign order id, a subscription since deleted — is a decision, not a
        // failure, and retrying it four more times would reach the same one.
        await _issuer.IssueForPaymentAsync(
            work.TenantId,
            work.AggregateId,
            work.CorrelationId,
            cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

/// <summary>
/// Renders and emails an issued document, or sweeps the ones that never got that far.
/// </summary>
public sealed class FinancialDocumentDeliveryWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionFinancialDocumentDeliveryService _delivery;

    public FinancialDocumentDeliveryWorkHandler(
        ISubscriptionFinancialDocumentDeliveryService delivery) =>
        _delivery = delivery;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.FinancialDocumentDelivery;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (string.IsNullOrWhiteSpace(work.AggregateId))
        {
            await _delivery.DeliverPendingAsync(work.TenantId, cancellationToken);

            return SubscriptionWorkOutcome.Completed();
        }

        // Retried rather than completed on failure, because a render or a mail publish that failed is
        // exactly the kind of thing that succeeds on the next attempt — and the document itself
        // counts its own attempts, so this cannot retry forever.
        return await _delivery.DeliverAsync(work.TenantId, work.AggregateId, cancellationToken)
            ? SubscriptionWorkOutcome.Completed()
            : SubscriptionWorkOutcome.Retry(
                "document_delivery_incomplete",
                "The document's PDF or email did not complete.");
    }
}

public sealed class OutboxPublicationWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionOutboxProcessor _outbox;

    public OutboxPublicationWorkHandler(ISubscriptionOutboxProcessor outbox) => _outbox = outbox;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.OutboxPublication;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _outbox.PublishDueAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}
