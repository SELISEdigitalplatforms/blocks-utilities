using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Spending, giving back and announcing credit grants: the half of credits the usage gate needs,
/// shared with the service that sells and grants them.
/// </summary>
/// <remarks>
/// Buckets are searched in the order a seat's recording should spend them: the seat's own credits
/// first, then the shared pool's. A pool on a seated plan is a top-up any seat may draw on; were it
/// spent only by organization-level recordings, which a seated plan rarely makes, it would be
/// credits paid for and never usable.
/// </remarks>
public sealed class CreditLedger
{
    private readonly ISubscriptionCreditGrantRepository _grants;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly ISubscriptionWorkScheduler _scheduler;

    public CreditLedger(
        ISubscriptionCreditGrantRepository grants,
        ISubscriptionUsageRepository usage,
        ISubscriptionRepository subscriptions,
        ISubscriptionOutboxEventFactory events,
        ISubscriptionWorkScheduler scheduler)
    {
        _grants = grants;
        _usage = usage;
        _subscriptions = subscriptions;
        _events = events;
        _scheduler = scheduler;
    }

    /// <summary>
    /// Takes up to <paramref name="quantity"/> from the live grants a recording in
    /// <paramref name="seatNumber"/> may spend, soonest-lost first.
    /// </summary>
    /// <remarks>
    /// Each take is a conditional write on one grant, so a grant another recording emptied a moment
    /// ago is simply skipped. The result says exactly what was taken from where, which is what
    /// <see cref="RestoreAsync"/> needs to undo it.
    /// </remarks>
    public async Task<CreditDraw> DrawAsync(
        SubscriptionDetail subscription,
        string meterKey,
        int? seatNumber,
        decimal quantity,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var taken = new List<CreditTake>();
        var left = quantity;

        foreach (var bucket in BucketsFor(seatNumber))
        {
            if (left <= 0)
            {
                break;
            }

            var live = await SpendableAsync(subscription, meterKey, bucket, nowUtc, cancellationToken);

            foreach (var grant in live)
            {
                if (left <= 0)
                {
                    break;
                }

                var amount = Math.Min(grant.Remaining, left);

                if (await _grants.TryConsumeAsync(subscription.TenantId, grant.ItemId, amount, nowUtc, cancellationToken)
                    is { } after)
                {
                    taken.Add(new CreditTake(after, amount));
                    left -= amount;
                }
            }
        }

        return new CreditDraw(quantity - left, taken);
    }

    /// <summary>Gives back everything a draw took, when the recording it paid for is refused.</summary>
    public async Task RestoreAsync(
        string tenantId,
        CreditDraw draw,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draw);

        foreach (var take in draw.Taken)
        {
            await _grants.RestoreAsync(tenantId, take.Grant.ItemId, take.Amount, nowUtc, cancellationToken);
        }
    }

    /// <summary>What a recording in <paramref name="seatNumber"/> could still spend from grants.</summary>
    public async Task<decimal> RemainingAsync(
        SubscriptionDetail subscription,
        string meterKey,
        int? seatNumber,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        decimal remaining = 0;

        foreach (var bucket in BucketsFor(seatNumber))
        {
            remaining += (await SpendableAsync(subscription, meterKey, bucket, nowUtc, cancellationToken))
                .Sum(grant => grant.Remaining);
        }

        return remaining;
    }

    /// <summary>
    /// Announces the bucket a draw emptied. Once per bucket, because a subscriber wants to know that
    /// their credits ran out, not that one of several purchases did.
    /// </summary>
    public async Task AnnounceExhaustedAsync(
        SubscriptionDetail subscription,
        CreditDraw draw,
        DateTime nowUtc,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(draw);

        foreach (var emptied in draw.Taken
                     .Where(take => take.Grant.Status == CreditGrantStatus.Exhausted)
                     .GroupBy(take => take.Grant.SeatNumber))
        {
            var last = emptied.Last().Grant;

            var left = await SpendableAsync(subscription, last.MeterKey, last.SeatNumber, nowUtc, cancellationToken);

            if (left.Count == 0)
            {
                await PublishAsync(
                    subscription, SubscriptionConstants.SubscriptionCreditsExhausted, last, correlationId, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Clears the usage alerts a credit grant has moved back out of reach in the window it lands
    /// in, so each can be reported again when it is crossed against the larger total.
    /// </summary>
    /// <remarks>
    /// Only the counter the grant's own bucket records against. ponytail: pool credits on a seated
    /// plan do not re-arm each seat's alerts; walk the seats' counters if subscribers ask for it.
    /// </remarks>
    public async Task RearmThresholdsAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        int? seatNumber,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(meter);

        if (meter.ThresholdPercents.Count == 0 ||
            !MeterPeriodResolver.TryGetPeriod(subscription, meter, nowUtc, out var period))
        {
            return;
        }

        var counter = await _usage.GetCounterAsync(
            subscription.TenantId,
            SubscriptionUsageCounter.CreateId(subscription.ItemId, meter.MeterKey, period.Key, seatNumber),
            cancellationToken);

        if (counter is not { LimitSnapshot: { } limit, NotifiedThresholds.Count: > 0 })
        {
            return;
        }

        var total = limit + counter.PurchasedConsumed +
            await RemainingAsync(subscription, meter.MeterKey, seatNumber, nowUtc, cancellationToken);

        var cleared = counter.NotifiedThresholds
            .Where(threshold => counter.Balance * 100 < total * threshold)
            .ToList();

        if (cleared.Count > 0)
        {
            await _usage.TryClearThresholdsAsync(subscription.TenantId, counter.ItemId, cleared, cancellationToken);
        }
    }

    /// <summary>Appends a credit event to the subscription's outbox and schedules it.</summary>
    public async Task PublishAsync(
        SubscriptionDetail subscription,
        string eventType,
        SubscriptionCreditGrant grant,
        string correlationId,
        CancellationToken cancellationToken,
        int? fromSeatNumber = null)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var outboxEvent = _events.CreateCreditEvent(subscription, eventType, grant, correlationId, fromSeatNumber);

        if (await _subscriptions.TryAppendEventAsync(
                subscription.TenantId, subscription.ItemId, outboxEvent, cancellationToken))
        {
            await _scheduler.ScheduleOutboxPublicationAsync(subscription, outboxEvent, cancellationToken);
        }
    }

    /// <summary>
    /// One bucket's grants that may be spent now, in the order they are spent.
    /// </summary>
    /// <remarks>
    /// The stored status alone is not enough: a grant the maintenance sweep has not reached yet can
    /// still read Active after its plan changed or its subscription ended. Every spend and move goes
    /// through here, so such a grant is refused the moment it lapses, not the next day.
    /// </remarks>
    public async Task<IReadOnlyList<SubscriptionCreditGrant>> SpendableAsync(
        SubscriptionDetail subscription,
        string meterKey,
        int? seatNumber,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var live = await _grants.ListLiveAsync(
            subscription.TenantId, subscription.ItemId, meterKey, seatNumber, nowUtc, cancellationToken);

        return [.. live.Where(grant => EffectiveStatus(grant, subscription, nowUtc) == CreditGrantStatus.Active)];
    }

    /// <summary>
    /// What a grant's status is at <paramref name="nowUtc"/>, whether or not the store has caught
    /// up with it yet.
    /// </summary>
    /// <param name="subscription">The grant's subscription as it stands, or null when it is gone.</param>
    /// <remarks>
    /// Forfeited when the subscription has ended, when its plan no longer has the grant's meter, or —
    /// for credits that carry forward — when the plan has changed since they were granted. A plan
    /// change re-anchors the usage schedule at the change, so a carry-forward grant from before the
    /// anchor is one the change has forfeited: the same test the plan's own carried-forward allowance
    /// is held to. An unpaid subscription keeps its credits, because it can still be paid and
    /// resumed.
    /// </remarks>
    public static CreditGrantStatus EffectiveStatus(
        SubscriptionCreditGrant grant,
        SubscriptionDetail? subscription,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(grant);

        if (grant.Status != CreditGrantStatus.Active)
        {
            return grant.Status;
        }

        if (grant.ExpiresAtUtc is { } expires && expires <= nowUtc)
        {
            return CreditGrantStatus.Expired;
        }

        if (subscription is null ||
            subscription.Status is SubscriptionStatus.Canceled or SubscriptionStatus.IncompleteExpired ||
            !subscription.Plan.Meters.Exists(meter =>
                string.Equals(meter.MeterKey, grant.MeterKey, StringComparison.Ordinal)) ||
            (grant.ExpiryPolicy == MeterResetPolicy.CarryForward &&
             grant.GrantedAtUtc < subscription.UsageSchedule.AnchorInstantUtc))
        {
            return CreditGrantStatus.Forfeited;
        }

        return CreditGrantStatus.Active;
    }

    /// <summary>
    /// Brings stored grant statuses into line with <see cref="EffectiveStatus"/> for one batch of a
    /// tenant's grants that are due a look. Returns how many it closed.
    /// </summary>
    public async Task<int> MaintainAsync(
        string tenantId,
        DateTime nowUtc,
        TimeSpan checkInterval,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var due = await _grants.ListDueForCheckAsync(
            tenantId, nowUtc, nowUtc - checkInterval, Math.Max(1, batchSize), cancellationToken);
        var closed = 0;

        foreach (var bySubscription in due.GroupBy(grant => grant.SubscriptionId, StringComparer.Ordinal))
        {
            var subscription = await _subscriptions.GetByIdAsync(tenantId, bySubscription.Key, cancellationToken);
            var stillLive = new List<string>();

            foreach (var grant in bySubscription)
            {
                var status = EffectiveStatus(grant, subscription, nowUtc);

                if (status == CreditGrantStatus.Active)
                {
                    stillLive.Add(grant.ItemId);
                }
                else if (await _grants.TryCloseAsync(tenantId, grant.ItemId, status, nowUtc, cancellationToken))
                {
                    closed++;
                }
            }

            await _grants.MarkCheckedAsync(tenantId, stillLive, nowUtc, cancellationToken);
        }

        return closed;
    }

    private static int?[] BucketsFor(int? seatNumber) =>
        seatNumber is null ? [null] : [seatNumber, null];
}

/// <summary>What one draw took: the total, and each grant as it stood after its take.</summary>
public sealed record CreditDraw(decimal Drawn, IReadOnlyList<CreditTake> Taken);

public sealed record CreditTake(SubscriptionCreditGrant Grant, decimal Amount);
