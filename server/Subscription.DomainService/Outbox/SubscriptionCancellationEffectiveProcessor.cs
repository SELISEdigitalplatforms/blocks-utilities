using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// The periodic sweep that finishes a scheduled cancellation once the period it was waiting out
/// has actually ended.
/// </summary>
/// <remarks>
/// <see cref="SubscriptionCancellationService"/> only ever records the intention — a subscriber
/// who cancels keeps what they paid for, so nothing about the interactive request can be the
/// thing that stops access on a date nobody is watching a screen for. This sweep is that watcher.
/// </remarks>
public sealed class SubscriptionCancellationEffectiveProcessor : ISubscriptionCancellationEffectiveProcessor
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionCancellationEffectiveProcessor> _logger;
    private readonly TimeProvider _time;
    private readonly IUsagePeriodClosureRepository? _closures;

    public SubscriptionCancellationEffectiveProcessor(
        ISubscriptionRepository subscriptions,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionCancellationEffectiveProcessor> logger,
        TimeProvider? time = null,
        IUsagePeriodClosureRepository? closures = null)
    {
        _subscriptions = subscriptions;
        _events = events;
        _cache = cache;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _closures = closures;
    }

    public async Task<int> ProcessDueAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _subscriptions.ListDueForCancellationAsync(
            tenantId,
            now,
            Math.Max(1, options.CancellationBatchSize),
            cancellationToken);

        var ended = 0;

        foreach (var subscription in due)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantHash"] = PaymentLogValue.Hash(tenantId),
                ["SubscriptionHash"] = PaymentLogValue.Hash(subscription.ItemId)
            });

            if (await TryFinalizeAsync(subscription, cancellationToken))
            {
                ended++;
            }
        }

        return ended;
    }

    /// <summary>
    /// Finishes one subscription's scheduled cancellation — the shared body behind both the
    /// tenant-wide sweep above and a targeted work item naming this subscription specifically.
    /// </summary>
    /// <remarks>
    /// Kept here rather than duplicated at the targeted call site, so a change to what "finishing
    /// a cancellation" means — a new field to clear, a different event — cannot land in one path
    /// and be forgotten in the other; the handlers' own doc comment is explicit that reimplementing
    /// this kind of logic per caller gives the same money two sets of rules.
    /// </remarks>
    public async Task<bool> TryFinalizeAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        // The instant entitlement was actually promised to stop — the period end the
        // subscriber was shown while this was still "Scheduled" — not whenever this worker
        // happens to run. A late pass must not silently extend service past what was promised,
        // and re-dating it to "now" on every retry would also break the invoice's own
        // idempotency: two different runs would each freeze a different end and price a
        // different window for the same period key.
        var effectiveAtUtc = subscription.CurrentPeriodEndUtc;
        ClosureReservation? closure = null;

        if (_closures is not null)
        {
            var periodKey = PeriodKey.Create(
                subscription.UsageSchedule.Interval,
                subscription.CurrentUsagePeriodStartUtc);
            var closeOperationId =
                $"cancellation-close:{subscription.ItemId}:{effectiveAtUtc.Ticks}";

            // Not caught: a storage failure here must stop this subscription's finalization for
            // this pass rather than let the transition below proceed behind a reservation that
            // was never actually taken — the sweep simply revisits it next pass.
            var outcome = await _closures.TryReserveClosingAsync(
                subscription.TenantId,
                subscription.ItemId,
                periodKey,
                effectiveAtUtc,
                closeOperationId,
                cancellationToken);

            if (outcome != ClosureReservationOutcome.Reserved)
            {
                // A different cancellation outcome — a different boundary — already reserved
                // this period. Not this pass's to finalize; the query that found it due will
                // simply stop surfacing it once whichever write actually wins commits.
                return false;
            }

            closure = new ClosureReservation(periodKey, closeOperationId);
        }

        // A lost compare-and-set here means another worker — or an interactive escalation
        // request racing this very pass — already ended it. Its outcome stands either way,
        // so this is not an error; the caller simply moves on.
        var applied = await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            new SubscriptionTransition(subscription.Status, SubscriptionStatus.Canceled)
            {
                CancelAtPeriodEnd = false,
                CanCancelImmediately = false,
                EndedAtUtc = effectiveAtUtc,
                ClearNextFeeBillingAt = true,
                ClearNextUsageBillingAt = true,
                // The still-open final window would otherwise never be rated: the usage sweep
                // only ever looked at live subscriptions, and this write is what takes this
                // one out of that set. Queuing it here, atomically with the status change, is
                // what a plan change does with its own outgoing window — captured in the same
                // compare-and-set that would otherwise let it be forgotten.
                OutgoingUsagePeriod = OutgoingUsagePeriodOf(subscription, effectiveAtUtc),
                Event = _events.Create(
                    subscription,
                    SubscriptionConstants.SubscriptionCanceled,
                    subscription.CorrelationId)
            },
            cancellationToken);

        if (closure is not null)
        {
            if (applied)
            {
                var outcome = await _closures!.TryCommitClosingAsync(
                    subscription.TenantId, subscription.ItemId, closure.PeriodKey,
                    closure.CloseOperationId, cancellationToken);

                if (outcome is not (ClosureCommitOutcome.Committed or ClosureCommitOutcome.AlreadyCommitted))
                {
                    // The transition just won, but the reservation could not be committed to
                    // Closing — left CloseReserved forever otherwise, blocking final invoicing.
                    // Not retried here: the tenant repair sweep's stale-reservation reconciliation
                    // recovers any reservation left stuck like this once it ages past its timeout.
                    _logger.LogWarning(
                        "A usage closure reservation could not be committed after its " +
                        "cancellation won SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey} " +
                        "Outcome={Outcome}",
                        PaymentLogValue.Hash(subscription.ItemId),
                        PaymentLogValue.Label(closure.PeriodKey),
                        outcome);
                }
            }
            else
            {
                // Another worker's compare-and-set won this transition, but it did so under a
                // different close operation (its own boundary read may have differed, or it was
                // a genuinely unrelated write) — this reservation must not linger and refuse
                // usage for a finalization this pass did not actually perform.
                await _closures!.TryReleaseReservationAsync(
                    subscription.TenantId, subscription.ItemId, closure.PeriodKey,
                    closure.CloseOperationId, cancellationToken);
            }
        }

        if (!applied)
        {
            return false;
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        return true;
    }

    private sealed record ClosureReservation(string PeriodKey, string CloseOperationId);

    /// <summary>
    /// Freezes the subscription's current usage window exactly as a plan change freezes its own
    /// outgoing one, so the rating sweep can price it after status has already moved on.
    /// </summary>
    /// <remarks>
    /// Cut to <paramref name="effectiveAtUtc"/> rather than left at the window's own natural end:
    /// entitlement stopped there, and an invoice that priced usage through the later, uncut end
    /// would be claiming to cover service the subscriber never actually had.
    /// </remarks>
    private static PendingUsagePeriod OutgoingUsagePeriodOf(
        SubscriptionDetail subscription,
        DateTime effectiveAtUtc) => new()
    {
        PeriodKey = PeriodKey.Create(
            subscription.UsageSchedule.Interval,
            subscription.CurrentUsagePeriodStartUtc),
        PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
        PeriodEndUtc = effectiveAtUtc,
        Plan = subscription.Plan,
        Price = subscription.Price,
        CurrencyCode = subscription.CurrencyCode,
        CorrelationId = subscription.CorrelationId
    };
}
