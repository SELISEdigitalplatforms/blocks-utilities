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

public sealed class CancellationEffectiveWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionCancellationEffectiveProcessor _cancellations;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly TimeProvider _time;

    public CancellationEffectiveWorkHandler(
        ISubscriptionCancellationEffectiveProcessor cancellations,
        ISubscriptionRepository subscriptions,
        TimeProvider? time = null)
    {
        _cancellations = cancellations;
        _subscriptions = subscriptions;
        _time = time ?? TimeProvider.System;
    }

    public SubscriptionWorkType WorkType => SubscriptionWorkType.CancellationEffective;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(work.AggregateId))
        {
            await _cancellations.ProcessDueAsync(work.TenantId, cancellationToken);

            return SubscriptionWorkOutcome.Completed();
        }

        // Read from the tenant's own database, never trusted from the scheduling document. The two
        // share no transaction, so this item may be describing a subscription that has since been
        // escalated to immediate, re-cancelled, or already finished by the tenant sweep.
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

        if (!subscription.CancelAtPeriodEnd)
        {
            // Already ended (escalation, or the sweep beat this item to it), or the schedule was
            // never there — either way, nothing this item names is still waiting.
            return SubscriptionWorkOutcome.Completed();
        }

        if (subscription.CurrentPeriodEndUtc > _time.GetUtcNow().UtcDateTime)
        {
            // Not due yet — this item was scheduled ahead of the boundary it names.
            return SubscriptionWorkOutcome.Completed();
        }

        await _cancellations.TryFinalizeAsync(subscription, cancellationToken);

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

    public FinancialDocumentIssueWorkHandler(ISubscriptionFinancialDocumentIssuer issuer) =>
        _issuer = issuer;

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
            // Drains whatever that subscription owes rather than one named document, so a trial
            // invoice and a credit note recorded moments apart are both written by one visit — and a
            // subscription that has since been deleted is simply nothing to do rather than a failure.
            await _issuer.IssueForSubscriptionAsync(
                work.TenantId,
                work.AggregateId,
                work.CorrelationId,
                cancellationToken);

            return SubscriptionWorkOutcome.Completed();
        }

        // Not completed whatever the answer, which is what this used to do. This item names one
        // payment that our own announcement said was a subscription charge, so "no document" is only
        // finished business for the reasons that are genuinely finished. Completing the rest left the
        // queue draining, every item succeeding, and nobody invoiced — the production failure this
        // whole design exists to remove, in a form that is harder to see than the original.
        var result = await _issuer.IssueForPaymentAsync(
            work.TenantId,
            work.AggregateId,
            work.CorrelationId,
            cancellationToken);

        if (result.IsSettledDecision)
        {
            return SubscriptionWorkOutcome.Completed();
        }

        // A payment that has not settled yet is usually a webhook that has not landed. Retried, and
        // if it never settles the attempts run out and the item dead-letters, which is visible.
        if (result.Outcome == FinancialDocumentIssueOutcome.PaymentNotSettled)
        {
            return SubscriptionWorkOutcome.Retry(
                "subscription_document_payment_not_settled",
                "The payment this document would describe has not settled yet.");
        }

        // An unrecognised charge or a missing subscription on an item that named this payment means
        // the announcement and the payment disagree. Retrying reaches the same answer, so it is
        // dead-lettered for a person to look at rather than being buried as a success.
        return SubscriptionWorkOutcome.Permanent(
            result.Outcome == FinancialDocumentIssueOutcome.UnknownCharge
                ? "subscription_document_charge_unrecognized"
                : "subscription_document_subscription_missing",
            $"No document could be issued for this payment: {result.Outcome}.");
    }
}

/// <summary>
/// Renders and emails an issued document, or sweeps the ones that never got that far.
/// </summary>
/// <remarks>
/// Checks <see cref="IFinancialDocumentRendererHealth"/> before calling the delivery service at
/// all, and this is deliberately the only handler that does. A renderer probe failing here must
/// not touch <see cref="ISubscriptionFinancialDocumentDeliveryService"/> — every path through it
/// that fails a render calls <c>RecordDeliveryFailureAsync</c>, which spends one of the document's
/// own limited delivery attempts and can abandon it outright once those run out. That budget
/// exists to give up on a document whose <em>own</em> template or data is unrenderable; spending it
/// on an outage that has nothing to do with any particular document would abandon real invoices for
/// a reason that was never theirs. Skipping the call entirely leaves every pending document exactly
/// as due as it was, so the repair sweep keeps finding it and delivery resumes for all of them the
/// moment the gate reopens — see <see cref="IFinancialDocumentRendererHealth"/>'s remarks.
/// </remarks>
public sealed class FinancialDocumentDeliveryWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionFinancialDocumentDeliveryService _delivery;
    private readonly IFinancialDocumentRendererHealth _rendererHealth;

    public FinancialDocumentDeliveryWorkHandler(
        ISubscriptionFinancialDocumentDeliveryService delivery,
        IFinancialDocumentRendererHealth rendererHealth)
    {
        _delivery = delivery;
        _rendererHealth = rendererHealth;
    }

    public SubscriptionWorkType WorkType => SubscriptionWorkType.FinancialDocumentDelivery;

    public Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (!_rendererHealth.IsHealthy)
        {
            // Retried rather than dead-lettered: this work item's own attempt budget still ticks
            // down on the queue's usual backoff, but that is cheap infrastructure retry, not the
            // document's delivery-attempt budget, which nothing here has touched. If an outage
            // outlasts this item's attempts and it dead-letters, the repair sweep re-announces the
            // still-undelivered document on its own next pass — see this handler's own remarks.
            return Task.FromResult(SubscriptionWorkOutcome.Retry(
                "financial_document_renderer_unhealthy",
                "The PDF renderer is currently unhealthy; document delivery is paused until it " +
                "recovers."));
        }

        return ExecuteWhileHealthyAsync(work, cancellationToken);
    }

    private async Task<SubscriptionWorkOutcome> ExecuteWhileHealthyAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(work.AggregateId))
        {
            await _delivery.DeliverPendingAsync(work.TenantId, cancellationToken);

            return SubscriptionWorkOutcome.Completed();
        }

        // Retried rather than completed on failure, because a render or a mail publish that failed is
        // exactly the kind of thing that succeeds on the next attempt — and the document itself
        // counts its own attempts, so this cannot retry forever.
        return await _delivery.DeliverAsync(
                work.TenantId,
                work.AggregateId,
                cancellationToken,
                work.ItemId,
                work.AttemptCount)
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
