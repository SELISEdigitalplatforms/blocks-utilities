using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private readonly ICreditOfferRepository? _offers;
    private readonly IOrganizationCreditOutboxProcessor? _organizationOutbox;
    private readonly ILogger? _logger;

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <param name="offers">
    /// Spec 005. Without them an organization holds no standalone credits and the gate reads none.
    /// </param>
    /// <param name="organizationOutbox">
    /// Publishes an organization's credit events as soon as they are written. Absent, they stay on
    /// the grant for the sweep, which is where a failed publish goes anyway.
    /// </param>
    public CreditLedger(
        ISubscriptionCreditGrantRepository grants,
        ISubscriptionUsageRepository usage,
        ISubscriptionRepository subscriptions,
        ISubscriptionOutboxEventFactory events,
        ISubscriptionWorkScheduler scheduler,
        ICreditOfferRepository? offers = null,
        IOrganizationCreditOutboxProcessor? organizationOutbox = null,
        ILogger<CreditLedger>? logger = null)
    {
        _grants = grants;
        _usage = usage;
        _subscriptions = subscriptions;
        _events = events;
        _scheduler = scheduler;
        _offers = offers;
        _organizationOutbox = organizationOutbox;
        _logger = logger;
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
                     .Where(take => take.Grant.Status == CreditGrantStatus.Exhausted && !take.Grant.IsOrganizationOwned)
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

        // An organization's own credits (spec 005) answer to no subscription: nothing a plan
        // change, a cancellation or a meter dropped from a plan does can forfeit them.
        if (grant.IsOrganizationOwned)
        {
            return CreditGrantStatus.Active;
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

        foreach (var bySubscription in due.GroupBy(grant => grant.SubscriptionId ?? string.Empty, StringComparer.Ordinal))
        {
            var subscription = bySubscription.Key.Length == 0
                ? null
                : await _subscriptions.GetByIdAsync(tenantId, bySubscription.Key, cancellationToken);
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

    // ---- an organization's own credits (spec 005) -----------------------------------------

    /// <summary>
    /// How an organization's standalone credits for a meter rank against what a subscription
    /// provides, or null when there is no offer and so no such credits to rank.
    /// </summary>
    public async Task<CreditSpendOrder?> SpendOrderAsync(
        string tenantId,
        string organizationId,
        string meterKey,
        CancellationToken cancellationToken) =>
        _offers is null
            ? null
            : (await _offers.ResolveAsync(tenantId, organizationId, meterKey, cancellationToken))?.SpendOrder;

    /// <summary>The organization's live standalone grants for one meter, in the order they are spent.</summary>
    public async Task<IReadOnlyList<SubscriptionCreditGrant>> SpendableForOrganizationAsync(
        string tenantId,
        string organizationId,
        string meterKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var live = await _grants.ListLiveForOrganizationAsync(tenantId, organizationId, meterKey, nowUtc, cancellationToken);

        return [.. live.Where(grant => EffectiveStatus(grant, null, nowUtc) == CreditGrantStatus.Active)];
    }

    /// <summary>Takes up to <paramref name="quantity"/> from the organization's own grants, soonest-lost first.</summary>
    public async Task<CreditDraw> DrawFromOrganizationAsync(
        string tenantId,
        string organizationId,
        string meterKey,
        decimal quantity,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var taken = new List<CreditTake>();
        var left = quantity;

        foreach (var grant in await SpendableForOrganizationAsync(tenantId, organizationId, meterKey, nowUtc, cancellationToken))
        {
            if (left <= 0)
            {
                break;
            }

            var amount = Math.Min(grant.Remaining, left);

            if (await _grants.TryConsumeAsync(tenantId, grant.ItemId, amount, nowUtc, cancellationToken) is { } after)
            {
                taken.Add(new CreditTake(after, amount));
                left -= amount;
            }
        }

        return new CreditDraw(quantity - left, taken);
    }

    /// <summary>What the organization could still spend from its standalone credits, per meter.</summary>
    public async Task<IReadOnlyDictionary<string, decimal>> OrganizationRemainingByMeterAsync(
        string tenantId,
        string organizationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var grants = await _grants.ListForOrganizationAsync(
            tenantId, organizationId, null, CreditGrantStatus.Active, cancellationToken);

        return grants
            .Where(grant => EffectiveStatus(grant, null, nowUtc) == CreditGrantStatus.Active && grant.Remaining > 0)
            .GroupBy(grant => grant.MeterKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(grant => grant.Remaining), StringComparer.Ordinal);
    }

    public async Task<decimal> OrganizationRemainingAsync(
        string tenantId,
        string organizationId,
        string meterKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        (await SpendableForOrganizationAsync(tenantId, organizationId, meterKey, nowUtc, cancellationToken))
            .Sum(grant => grant.Remaining);

    /// <summary>Announces that a draw emptied the organization's credits for a meter, once.</summary>
    public async Task AnnounceOrganizationExhaustedAsync(
        CreditDraw draw,
        DateTime nowUtc,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draw);

        var last = draw.Taken.LastOrDefault(take => take.Grant.Status == CreditGrantStatus.Exhausted)?.Grant;

        if (last is null || !last.IsOrganizationOwned)
        {
            return;
        }

        var left = await SpendableForOrganizationAsync(
            last.TenantId, last.OrganizationId, last.MeterKey, nowUtc, cancellationToken);

        if (left.Count == 0)
        {
            await PublishOrganizationAsync(
                last, Utilities.SubscriptionConstants.OrganizationCreditsExhausted, correlationId, cancellationToken);
        }
    }

    /// <summary>
    /// The outbox entry an organization credit event is stored as (spec 005): on the grant it is
    /// about, under one deduplication key per grant and event type, since a grant is bought or
    /// given exactly once and used up at most once.
    /// </summary>
    /// <param name="purchase">
    /// For a purchase, what the confirmation mail needs: the figures, the meter's name, and the
    /// billing account whose contact is written to.
    /// </param>
    public static SubscriptionOutboxEvent OrganizationEvent(
        SubscriptionCreditGrant grant,
        string eventType,
        string correlationId,
        SubscriptionCreditPurchase? purchase = null,
        DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var occurredAt = nowUtc ?? DateTime.UtcNow;
        var payload = new SubscriptionLifecycleEvent
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = eventType,
            TenantId = grant.TenantId,
            OrganizationId = grant.OrganizationId,
            MeterKey = grant.MeterKey,
            CreditGrantId = grant.ItemId,
            CreditQuantity = grant.Quantity,
            CreditRemaining = grant.Remaining,
            CreditExpiresAtUtc = grant.ExpiresAtUtc,
            CreditPurchaseId = purchase?.ItemId,
            MeterDisplayName = purchase?.MeterDisplayName,
            CreditTotalMinor = purchase?.TotalMinor,
            CurrencyCode = purchase?.CurrencyCode,
            BillingAccountId = purchase?.BillingAccountId,
            PackCode = purchase?.PackCode,
            CorrelationId = correlationId,
            OccurredAtUtc = occurredAt
        };

        return new SubscriptionOutboxEvent
        {
            EventId = payload.EventId,
            EventType = eventType,
            DeduplicationKey = OrganizationEventKey(grant, eventType),
            Payload = JsonSerializer.Serialize(payload, PayloadOptions),
            CorrelationId = correlationId,
            CreatedAtUtc = occurredAt
        };
    }

    private static string OrganizationEventKey(SubscriptionCreditGrant grant, string eventType) =>
        $"{grant.ItemId}:{eventType}";

    /// <summary>
    /// Makes sure an organization credit event is on its grant, then publishes what the grant is
    /// still owed.
    /// </summary>
    /// <remarks>
    /// A caller that already wrote the event into the grant's insert finds it here and skips the
    /// append; one announcing a transition after the fact, such as exhaustion, appends it under its
    /// key, so a retried draw adds nothing. Publishing is attempted at once for a consumer's sake
    /// and left to the sweep if it fails: the event is already durable, and the caller's own
    /// operation — a purchase, a usage recording — must not fail for a broker that is down.
    /// </remarks>
    public async Task PublishOrganizationAsync(
        SubscriptionCreditGrant grant,
        string eventType,
        string correlationId,
        CancellationToken cancellationToken,
        SubscriptionCreditPurchase? purchase = null)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var key = OrganizationEventKey(grant, eventType);

        if (!grant.OutboxEvents.Exists(outboxEvent => outboxEvent.DeduplicationKey == key))
        {
            var outboxEvent = OrganizationEvent(grant, eventType, correlationId, purchase);

            if (await _grants.TryAppendEventAsync(grant.TenantId, grant.ItemId, outboxEvent, cancellationToken) &&
                !grant.OutboxEvents.Exists(existing => existing.DeduplicationKey == key))
            {
                // Mirrored onto the copy in hand so the publish below sees it without a re-read.
                grant.OutboxEvents.Add(outboxEvent);
            }
        }

        if (_organizationOutbox is null)
        {
            return;
        }

        try
        {
            await _organizationOutbox.PublishDueForGrantAsync(grant, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(
                exception,
                "An organization credit event was left for the sweep EventType={EventType} GrantId={GrantId}",
                eventType,
                grant.ItemId);
        }
    }
}

/// <summary>What one draw took: the total, and each grant as it stood after its take.</summary>
public sealed record CreditDraw(decimal Drawn, IReadOnlyList<CreditTake> Taken);

public sealed record CreditTake(SubscriptionCreditGrant Grant, decimal Amount);
