using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionRepository : ISubscriptionRepository
{
    /// <summary>
    /// Statuses that grant something and that entitlement treats as current. The signup
    /// reservation index additionally includes Incomplete, since checkout must be exclusive
    /// before either customer pays.
    /// </summary>
    private static readonly SubscriptionStatus[] LiveStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
        SubscriptionStatus.PastDue
    ];

    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Subscriptions(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateSubscriptionIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<bool> TryCreateAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        try
        {
            await EnsureIndexesAsync(subscription.TenantId, cancellationToken);

            await Subscriptions(subscription.TenantId)
                .InsertOneAsync(subscription, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<SubscriptionDetail?> GetAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.OrganizationId,
                    organizationId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.ItemId,
                    subscriptionId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionDetail?> GetByIdAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.ItemId,
                    subscriptionId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionDetail?> GetLiveAsync(
        string tenantId,
        string organizationId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(BuildLiveFilter(tenantId, organizationId, nowUtc))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionDetail?> GetIncompleteAsync(
        string tenantId,
        string organizationId,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.OrganizationId,
                    organizationId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.Status,
                    SubscriptionStatus.Incomplete)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionDetail?> GetByOrderIdAsync(
        string tenantId,
        string orderId,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.OrderId,
                    orderId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> AnySubscriberAsync(
        string tenantId,
        string planId,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.Plan.PlanId,
                    planId)))
            .Project(subscription => subscription.ItemId)
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken) is not null;

    public async Task<bool> TryTransitionAsync(
        string tenantId,
        string subscriptionId,
        SubscriptionTransition transition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var filter = Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.ItemId,
                subscriptionId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.Status,
                transition.ExpectedStatus));

        if (transition.RequireNoSettlementReservation)
        {
            filter = Builders<SubscriptionDetail>.Filter.And(filter, NoSettlementReservationFilter());
        }

        if (transition.RequireCancellationNotAlreadyScheduled)
        {
            filter = Builders<SubscriptionDetail>.Filter.And(
                filter,
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.CancelAtPeriodEnd,
                    false));
        }

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            filter,
            BuildTransitionUpdate(transition),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryUpdateQuantityAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        string itemKey,
        long quantity,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.ItemId,
                subscriptionId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.Version,
                expectedVersion),
            Builders<SubscriptionDetail>.Filter.ElemMatch(
                subscription => subscription.QuantityItems,
                item => item.ItemKey == itemKey));

        var update = Builders<SubscriptionDetail>.Update
            .Set("QuantityItems.$[item].Quantity", quantity)
            .Inc(subscription => subscription.Version, 1)
            .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow);

        var options = new UpdateOptions
        {
            ArrayFilters =
            [
                new BsonDocumentArrayFilterDefinition<SubscriptionQuantityItem>(
                    new BsonDocument("item.ItemKey", itemKey))
            ]
        };

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            filter,
            update,
            options,
            cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryChangePlanAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        string? reservationId,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> newQuantityItems,
        SubscriptionPlanSchedule newSchedule,
        PendingUsagePeriod outgoingUsagePeriod,
        long newCreditBalanceMinor,
        string? planChangePaymentDetailId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken,
        SubscriptionDocumentSource? documentSource = null)
    {
        ArgumentNullException.ThrowIfNull(newPlan);
        ArgumentNullException.ThrowIfNull(newPrice);
        ArgumentNullException.ThrowIfNull(newQuantityItems);
        ArgumentNullException.ThrowIfNull(newSchedule);
        ArgumentNullException.ThrowIfNull(outgoingUsagePeriod);
        ArgumentNullException.ThrowIfNull(outboxEvent);

        // Addressed by the reservation when one is being promoted: the money has already moved,
        // so a concurrent change that happens to bump the version must not be able to strand terms
        // the subscriber has paid for. Addressed by version otherwise, and then only while no
        // reservation is held — a settlement in flight has already half-changed this subscription.
        var filter = reservationId is { Length: > 0 }
            ? ReservationFilter(tenantId, subscriptionId, reservationId)
            : Builders<SubscriptionDetail>.Filter.And(
                VersionedFilter(tenantId, subscriptionId, expectedVersion),
                NoSettlementReservationFilter());

        var update = Builders<SubscriptionDetail>.Update
            .Set(subscription => subscription.SettlementReservation, null)
            .Set(subscription => subscription.Plan, newPlan)
            .Set(subscription => subscription.Price, newPrice)
            .Set(subscription => subscription.QuantityItems, newQuantityItems)
            .Set(subscription => subscription.FeeSchedule, newSchedule.FeeSchedule)
            .Set(subscription => subscription.CurrentPeriodStartUtc, newSchedule.CurrentPeriodStartUtc)
            .Set(subscription => subscription.CurrentPeriodEndUtc, newSchedule.CurrentPeriodEndUtc)
            .Set(subscription => subscription.NextFeeBillingAtUtc, newSchedule.NextFeeBillingAtUtc)
            .Set(subscription => subscription.UsageSchedule, newSchedule.UsageSchedule)
            .Set(subscription => subscription.CurrentUsagePeriodStartUtc, newSchedule.CurrentUsagePeriodStartUtc)
            .Set(subscription => subscription.CurrentUsagePeriodEndUtc, newSchedule.CurrentUsagePeriodEndUtc)
            .Set(subscription => subscription.NextUsageBillingAtUtc, newSchedule.NextUsageBillingAtUtc)
            .Set(subscription => subscription.CreditBalanceMinor, newCreditBalanceMinor)
            .Push(subscription => subscription.PendingUsagePeriods, outgoingUsagePeriod)
            .Inc(subscription => subscription.Version, 1)
            .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow)
            .Push(subscription => subscription.OutboxEvents, outboxEvent);

        if (documentSource is not null)
        {
            // In this write or nowhere. A downgrade banks its credit here and charges nothing, so
            // there is no payment left behind to reconstruct the credit note from and no balance
            // that can say which change moved it - the obligation has to be as atomic as the credit.
            update = update.Push(
                subscription => subscription.PendingDocumentSources,
                documentSource);
        }

        if (planChangePaymentDetailId is { Length: > 0 })
        {
            update = update.Set(
                subscription => subscription.LastRenewalPaymentDetailId,
                planChangePaymentDetailId);
        }

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryApplyQuantityChangeAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        List<SubscriptionQuantityItem> newQuantityItems,
        long newCreditBalanceMinor,
        string? quantityChangePaymentDetailId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken,
        SubscriptionDocumentSource? documentSource = null)
    {
        ArgumentNullException.ThrowIfNull(newQuantityItems);
        ArgumentNullException.ThrowIfNull(outboxEvent);

        var update = Builders<SubscriptionDetail>.Update
            .Set(subscription => subscription.QuantityItems, newQuantityItems)
            .Set(subscription => subscription.CreditBalanceMinor, newCreditBalanceMinor)
            // An applied change supersedes anything scheduled: the quantity it was scheduled
            // against no longer exists.
            .Set(subscription => subscription.PendingQuantityChange, null)
            .Inc(subscription => subscription.Version, 1)
            .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow)
            .Push(subscription => subscription.OutboxEvents, outboxEvent);

        if (documentSource is not null)
        {
            // Same reason as the plan change: a decrease banks credit and takes no payment, so this
            // is the only write that can carry the credit note's obligation.
            update = update.Push(
                subscription => subscription.PendingDocumentSources,
                documentSource);
        }

        if (quantityChangePaymentDetailId is { Length: > 0 })
        {
            update = update.Set(
                subscription => subscription.LastRenewalPaymentDetailId,
                quantityChangePaymentDetailId);
        }

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            Builders<SubscriptionDetail>.Filter.And(
                VersionedFilter(tenantId, subscriptionId, expectedVersion),
                NoSettlementReservationFilter()),
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryReserveSettlementAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        SettlementReservation claim,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            Builders<SubscriptionDetail>.Filter.And(
                VersionedFilter(tenantId, subscriptionId, expectedVersion),
                // One claim at a time: a second increase must not reserve units on top of an
                // increase that is already holding some and may yet be paid for.
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.SettlementReservation,
                    null)),
            Builders<SubscriptionDetail>.Update
                .Set(subscription => subscription.SettlementReservation, claim)
                .Inc(subscription => subscription.Version, 1)
                .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryPromoteQuantityReservationAsync(
        string tenantId,
        string subscriptionId,
        string reservationId,
        List<SubscriptionQuantityItem> newQuantityItems,
        long newCreditBalanceMinor,
        string? quantityChangePaymentDetailId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newQuantityItems);
        ArgumentNullException.ThrowIfNull(outboxEvent);

        var update = Builders<SubscriptionDetail>.Update
            .Set(subscription => subscription.QuantityItems, newQuantityItems)
            .Set(subscription => subscription.CreditBalanceMinor, newCreditBalanceMinor)
            // An applied change supersedes anything scheduled: the quantity it was scheduled
            // against no longer exists.
            .Set(subscription => subscription.PendingQuantityChange, null)
            .Set(subscription => subscription.SettlementReservation, null)
            .Inc(subscription => subscription.Version, 1)
            .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow)
            .Push(subscription => subscription.OutboxEvents, outboxEvent);

        if (quantityChangePaymentDetailId is { Length: > 0 })
        {
            update = update.Set(
                subscription => subscription.LastRenewalPaymentDetailId,
                quantityChangePaymentDetailId);
        }

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            ReservationFilter(tenantId, subscriptionId, reservationId),
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryReleaseSettlementAsync(
        string tenantId,
        string subscriptionId,
        string reservationId,
        CancellationToken cancellationToken)
    {
        var result = await Subscriptions(tenantId).UpdateOneAsync(
            ReservationFilter(tenantId, subscriptionId, reservationId),
            Builders<SubscriptionDetail>.Update
                .Set(subscription => subscription.SettlementReservation, null)
                .Inc(subscription => subscription.Version, 1)
                .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<SubscriptionDetail>> ListStaleSettlementsAsync(
        string tenantId,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Ne(
                    subscription => subscription.SettlementReservation,
                    null),
                Builders<SubscriptionDetail>.Filter.Lt(
                    subscription => subscription.SettlementReservation!.ReservedAtUtc,
                    olderThanUtc)))
            .Limit(limit)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// No quantity increase is mid-settlement.
    /// </summary>
    /// <remarks>
    /// Applied to <see cref="TryTransitionAsync"/> only when the transition asks for it — see
    /// <see cref="SubscriptionTransition.RequireNoSettlementReservation"/>, which renewals set and
    /// activation, cancellation and usage rating do not. A blanket lock there would let one
    /// unresolvable reservation stall a subscription's whole lifecycle.
    /// </remarks>
    private static FilterDefinition<SubscriptionDetail> NoSettlementReservationFilter() =>
        Builders<SubscriptionDetail>.Filter.Eq(
            subscription => subscription.SettlementReservation,
            null);

    /// <summary>One subscription holding one exact claim — the address a settled claim promotes at.</summary>
    private static FilterDefinition<SubscriptionDetail> ReservationFilter(
        string tenantId,
        string subscriptionId,
        string reservationId) =>
        Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.ItemId,
                subscriptionId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.SettlementReservation!.ReservationId,
                reservationId));

    public async Task<bool> TrySetPendingQuantityChangeAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        PendingQuantityChange pending,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pending);

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            Builders<SubscriptionDetail>.Filter.And(
                VersionedFilter(tenantId, subscriptionId, expectedVersion),
                NoSettlementReservationFilter()),
            Builders<SubscriptionDetail>.Update
                .Set(subscription => subscription.PendingQuantityChange, pending)
                .Inc(subscription => subscription.Version, 1)
                .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryClearPendingQuantityChangeAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await Subscriptions(tenantId).UpdateOneAsync(
            VersionedFilter(tenantId, subscriptionId, expectedVersion),
            Builders<SubscriptionDetail>.Update
                .Set(subscription => subscription.PendingQuantityChange, null)
                .Inc(subscription => subscription.Version, 1)
                .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryBumpPaymentMethodSetupAttemptAsync(
        string tenantId,
        string subscriptionId,
        int expectedAttempt,
        CancellationToken cancellationToken)
    {
        var result = await Subscriptions(tenantId).UpdateOneAsync(
            Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.ItemId,
                    subscriptionId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.Status,
                    SubscriptionStatus.Incomplete),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.PaymentMethodSetupAttempt,
                    expectedAttempt)),
            Builders<SubscriptionDetail>.Update
                .Set(subscription => subscription.PaymentMethodSetupAttempt, expectedAttempt + 1)
                .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    /// <summary>One subscription at one exact version — the compare half of a compare-and-set.</summary>
    private static FilterDefinition<SubscriptionDetail> VersionedFilter(
        string tenantId,
        string subscriptionId,
        int expectedVersion) =>
        Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.ItemId,
                subscriptionId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.Version,
                expectedVersion));

    public async Task<bool> TryRemovePendingUsagePeriodAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken)
    {
        var result = await Subscriptions(tenantId).UpdateOneAsync(
            Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(subscription => subscription.ItemId, subscriptionId)),
            Builders<SubscriptionDetail>.Update.PullFilter(
                subscription => subscription.PendingUsagePeriods,
                pending => pending.PeriodKey == periodKey),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<SubscriptionDetail>> ListStaleAsync(
        string tenantId,
        SubscriptionStatus status,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.Status,
                    status),
                Builders<SubscriptionDetail>.Filter.Lt(
                    subscription => subscription.CreatedAtUtc,
                    olderThanUtc)))
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionDetail>> ListDueForRenewalAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.In(
                    subscription => subscription.Status,
                    LiveStatuses),
                // Explicitly excludes null rather than relying on how $lte treats it: a
                // cancel-at-period-end subscription clears this field on purpose, and a
                // cross-type comparison including null in the range would renew it anyway.
                Builders<SubscriptionDetail>.Filter.Ne(
                    subscription => subscription.NextFeeBillingAtUtc,
                    null),
                Builders<SubscriptionDetail>.Filter.Lte(
                    subscription => subscription.NextFeeBillingAtUtc,
                    asOfUtc)))
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionDetail>> ListDueForCancellationAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.In(
                    subscription => subscription.Status,
                    LiveStatuses),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.CancelAtPeriodEnd,
                    true),
                Builders<SubscriptionDetail>.Filter.Lte(
                    subscription => subscription.CurrentPeriodEndUtc,
                    asOfUtc)))
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionDetail>> ListDueForUsageRatingAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Or(
                    Builders<SubscriptionDetail>.Filter.And(
                        Builders<SubscriptionDetail>.Filter.In(
                            subscription => subscription.Status,
                            LiveStatuses),
                        // Explicitly excludes null rather than relying on how $lte treats it —
                        // same reasoning as the renewal due-query: an immediately-canceled
                        // subscription clears this field on purpose.
                        Builders<SubscriptionDetail>.Filter.Or(
                            Builders<SubscriptionDetail>.Filter.SizeGt(
                                subscription => subscription.PendingUsagePeriods, 0),
                            Builders<SubscriptionDetail>.Filter.And(
                                Builders<SubscriptionDetail>.Filter.Ne(
                                    subscription => subscription.NextUsageBillingAtUtc, null),
                                Builders<SubscriptionDetail>.Filter.Lte(
                                    subscription => subscription.NextUsageBillingAtUtc, asOfUtc)))),
                    // A subscription that has already ended keeps showing up here for exactly as
                    // long as it still holds a final window nothing else will ever rate — the
                    // live branch above stops matching it the instant it leaves LiveStatuses, and
                    // there is no other sweep watching a Canceled subscription for unrated usage.
                    Builders<SubscriptionDetail>.Filter.And(
                        Builders<SubscriptionDetail>.Filter.Eq(
                            subscription => subscription.Status,
                            SubscriptionStatus.Canceled),
                        Builders<SubscriptionDetail>.Filter.SizeGt(
                            subscription => subscription.PendingUsagePeriods, 0)))))
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<bool> TryAppendEventAsync(
        string tenantId,
        string subscriptionId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outboxEvent);

        var filter = Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.ItemId,
                subscriptionId),
            NotAlreadyEmitted(outboxEvent.DeduplicationKey));

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            filter,
            Builders<SubscriptionDetail>.Update.Push(
                subscription => subscription.OutboxEvents,
                outboxEvent),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<SubscriptionOutboxEvent?> TryClaimEventAsync(
        string tenantId,
        string subscriptionId,
        string eventId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.ItemId,
                subscriptionId),
            Builders<SubscriptionDetail>.Filter.ElemMatch(
                subscription => subscription.OutboxEvents,
                outboxEvent =>
                    outboxEvent.EventId == eventId &&
                    outboxEvent.Status != SubscriptionOutboxStatus.Published &&
                    outboxEvent.Status != SubscriptionOutboxStatus.Abandoned &&
                    (outboxEvent.LeaseExpiresAtUtc == null ||
                     outboxEvent.LeaseExpiresAtUtc <= DateTime.UtcNow)));

        var update = Builders<SubscriptionDetail>.Update
            .Set("OutboxEvents.$[evt].Status", SubscriptionOutboxStatus.Processing)
            .Set("OutboxEvents.$[evt].LeaseId", leaseId)
            .Set("OutboxEvents.$[evt].LeaseExpiresAtUtc", leaseExpiresAtUtc);

        var updated = await Subscriptions(tenantId).FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<SubscriptionDetail>
            {
                ArrayFilters = EventArrayFilter(eventId),
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        return updated?.OutboxEvents
            .FirstOrDefault(outboxEvent =>
                string.Equals(outboxEvent.EventId, eventId, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<SubscriptionDetail>> ListWithDueEventsAsync(
        string tenantId,
        DateTime dueAtUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.ElemMatch(
                    subscription => subscription.OutboxEvents,
                    outboxEvent =>
                        outboxEvent.Status != SubscriptionOutboxStatus.Published &&
                        outboxEvent.Status != SubscriptionOutboxStatus.Abandoned &&
                        (outboxEvent.NextAttemptAtUtc == null ||
                         outboxEvent.NextAttemptAtUtc <= dueAtUtc))))
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task MarkEventPublishedAsync(
        string tenantId,
        string subscriptionId,
        string eventId,
        string leaseId,
        DateTime publishedAtUtc,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId).UpdateOneAsync(
            LeasedEventFilter(tenantId, subscriptionId, eventId, leaseId),
            Builders<SubscriptionDetail>.Update
                .Set("OutboxEvents.$[evt].Status", SubscriptionOutboxStatus.Published)
                .Set("OutboxEvents.$[evt].PublishedAtUtc", publishedAtUtc)
                .Set("OutboxEvents.$[evt].LeaseId", (string?)null)
                .Set("OutboxEvents.$[evt].LeaseExpiresAtUtc", (DateTime?)null),
            new UpdateOptions { ArrayFilters = EventArrayFilter(eventId) },
            cancellationToken);

    public async Task MarkEventFailedAsync(
        string tenantId,
        string subscriptionId,
        string eventId,
        string leaseId,
        SubscriptionOutboxStatus status,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string failureReason,
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId).UpdateOneAsync(
            LeasedEventFilter(tenantId, subscriptionId, eventId, leaseId),
            Builders<SubscriptionDetail>.Update
                .Set("OutboxEvents.$[evt].Status", status)
                .Set("OutboxEvents.$[evt].AttemptCount", attemptCount)
                .Set("OutboxEvents.$[evt].NextAttemptAtUtc", nextAttemptAtUtc)
                .Set("OutboxEvents.$[evt].LastError", Shorten(failureReason))
                .Set("OutboxEvents.$[evt].LeaseId", (string?)null)
                .Set("OutboxEvents.$[evt].LeaseExpiresAtUtc", (DateTime?)null),
            new UpdateOptions { ArrayFilters = EventArrayFilter(eventId) },
            cancellationToken);

    /// <summary>
    /// The live-subscription filter, exposed so tenant and organization scoping can be tested
    /// without a database. A wrong scope still returns plausible rows, which is exactly the
    /// kind of mistake an integration test will not notice.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="Utilities.SubscriptionLiveness.IsEffectivelyLive"/> — a scheduled
    /// cancellation stops matching the instant its promised <c>CurrentPeriodEndUtc</c> passes
    /// <paramref name="nowUtc"/>, independent of whether the finalizing worker has run yet.
    /// </remarks>
    public static FilterDefinition<SubscriptionDetail> BuildLiveFilter(
        string tenantId,
        string organizationId,
        DateTime nowUtc) =>
        Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.OrganizationId,
                organizationId),
            Builders<SubscriptionDetail>.Filter.In(
                subscription => subscription.Status,
                LiveStatuses),
            Builders<SubscriptionDetail>.Filter.Or(
                Builders<SubscriptionDetail>.Filter.Ne(
                    subscription => subscription.CancelAtPeriodEnd,
                    true),
                Builders<SubscriptionDetail>.Filter.Gt(
                    subscription => subscription.CurrentPeriodEndUtc,
                    nowUtc)));

    private static UpdateDefinition<SubscriptionDetail> BuildTransitionUpdate(
        SubscriptionTransition transition)
    {
        var update = Builders<SubscriptionDetail>.Update
            .Set(subscription => subscription.Status, transition.NewStatus)
            .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow)
            .Inc(subscription => subscription.Version, 1);

        if (transition.QuantityItems is { } quantityItems)
        {
            update = update.Set(
                subscription => subscription.QuantityItems,
                quantityItems);
        }

        if (transition.ClearPendingQuantityChange)
        {
            update = update.Set(subscription => subscription.PendingQuantityChange, null);
        }

        if (transition.ActivatedAtUtc is { } activatedAt)
        {
            update = update.Set(
                subscription => subscription.ActivatedAtUtc,
                activatedAt);
        }

        if (transition.CanceledAtUtc is { } canceledAt)
        {
            update = update.Set(
                subscription => subscription.CanceledAtUtc,
                canceledAt);
        }

        if (transition.EndedAtUtc is { } endedAt)
        {
            update = update.Set(subscription => subscription.EndedAtUtc, endedAt);
        }

        if (transition.CancelAtPeriodEnd is { } cancelAtPeriodEnd)
        {
            update = update.Set(
                subscription => subscription.CancelAtPeriodEnd,
                cancelAtPeriodEnd);
        }

        if (transition.CanCancelImmediately is { } canCancelImmediately)
        {
            update = update.Set(
                subscription => subscription.CanCancelImmediately,
                canCancelImmediately);
        }

        if (transition.OutgoingUsagePeriod is { } outgoingUsagePeriod)
        {
            update = update.Push(
                subscription => subscription.PendingUsagePeriods,
                outgoingUsagePeriod);
        }

        if (transition.CancellationReason is { } reason)
        {
            update = update.Set(
                subscription => subscription.CancellationReason,
                Shorten(reason));
        }

        if (transition.InitialPaymentDetailId is { } paymentDetailId)
        {
            update = update.Set(
                subscription => subscription.InitialPaymentDetailId,
                paymentDetailId);
        }

        if (transition.LastRenewalPaymentDetailId is { } renewalPaymentDetailId)
        {
            update = update.Set(
                subscription => subscription.LastRenewalPaymentDetailId,
                renewalPaymentDetailId);
        }

        if (transition.DunningAttemptCount is { } dunningAttemptCount)
        {
            update = update.Set(
                subscription => subscription.DunningAttemptCount,
                dunningAttemptCount);
        }

        if (transition.DiscountPeriodsApplied is { } discountPeriodsApplied)
        {
            update = update.Set(
                subscription => subscription.DiscountPeriodsApplied,
                discountPeriodsApplied);
        }

        if (transition.InitialChargeAmountMinor is { } initialChargeAmountMinor)
        {
            update = update.Set(
                subscription => subscription.InitialChargeAmountMinor,
                initialChargeAmountMinor);
        }

        if (transition.InitialChargeProrated is { } initialChargeProrated)
        {
            update = update.Set(
                subscription => subscription.InitialChargeProrated,
                initialChargeProrated);
        }

        if (transition.InitialChargeDiscountApplied is { } initialChargeDiscountApplied)
        {
            update = update.Set(
                subscription => subscription.InitialChargeDiscountApplied,
                initialChargeDiscountApplied);
        }

        // Written as a pair, and only alongside a prorated first charge — the fraction is
        // meaningless without both halves of it.
        if (transition.ProrationDays is { } prorationDays &&
            transition.ProrationTotalDays is { } prorationTotalDays)
        {
            update = update
                .Set(subscription => subscription.ProrationDays, prorationDays)
                .Set(subscription => subscription.ProrationTotalDays, prorationTotalDays);
        }

        if (transition.ClearPendingAnnualPeriod)
        {
            update = update.Set(subscription => subscription.PendingAnnualPeriod, null);
        }
        else if (transition.PendingAnnualPeriod is { } pendingAnnualPeriod)
        {
            update = update.Set(
                subscription => subscription.PendingAnnualPeriod,
                pendingAnnualPeriod);
        }
        else if (transition.MarkPendingAnnualPeriodPrepaid)
        {
            // Only the flag, so a concurrent writer that changed something else about the year
            // does not have its work replaced by a stale copy of the whole document.
            update = update.Set(
                subscription => subscription.PendingAnnualPeriod!.IsPrepaid,
                true);
        }

        if (transition.CreditBalanceMinor is { } creditBalanceMinor)
        {
            update = update.Set(
                subscription => subscription.CreditBalanceMinor,
                creditBalanceMinor);
        }

        if (transition.ClearPastDueSinceAt)
        {
            update = update.Set(subscription => subscription.PastDueSinceUtc, null);
        }
        else if (transition.PastDueSinceUtc is { } pastDueSince)
        {
            update = update.Set(subscription => subscription.PastDueSinceUtc, pastDueSince);
        }

        if (transition.CurrentPeriodStartUtc is { } periodStart)
        {
            update = update.Set(
                subscription => subscription.CurrentPeriodStartUtc,
                periodStart);
        }

        if (transition.CurrentPeriodEndUtc is { } periodEnd)
        {
            update = update.Set(
                subscription => subscription.CurrentPeriodEndUtc,
                periodEnd);
        }

        if (transition.ClearNextFeeBillingAt)
        {
            update = update.Set(
                subscription => subscription.NextFeeBillingAtUtc,
                null);
        }
        else if (transition.NextFeeBillingAtUtc is { } nextBilling)
        {
            update = update.Set(
                subscription => subscription.NextFeeBillingAtUtc,
                nextBilling);
        }

        if (transition.CurrentUsagePeriodStartUtc is { } usagePeriodStart)
        {
            update = update.Set(
                subscription => subscription.CurrentUsagePeriodStartUtc,
                usagePeriodStart);
        }

        if (transition.CurrentUsagePeriodEndUtc is { } usagePeriodEnd)
        {
            update = update.Set(
                subscription => subscription.CurrentUsagePeriodEndUtc,
                usagePeriodEnd);
        }

        if (transition.ClearNextUsageBillingAt)
        {
            update = update.Set(
                subscription => subscription.NextUsageBillingAtUtc,
                null);
        }
        else if (transition.NextUsageBillingAtUtc is { } nextUsageBilling)
        {
            update = update.Set(
                subscription => subscription.NextUsageBillingAtUtc,
                nextUsageBilling);
        }

        return transition.Event is null
            ? update
            : update.Push(subscription => subscription.OutboxEvents, transition.Event);
    }

    public async Task<bool> TryAppendDocumentSourceAsync(
        string tenantId,
        string subscriptionId,
        SubscriptionDocumentSource documentSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentSource);

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.ItemId,
                    subscriptionId),
                NoSourceFor(documentSource.SourceKey)),
            Builders<SubscriptionDetail>.Update.Push(
                subscription => subscription.PendingDocumentSources,
                documentSource),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryConsumeDocumentSourceAsync(
        string tenantId,
        string subscriptionId,
        string sourceKey,
        CancellationToken cancellationToken)
    {
        var result = await Subscriptions(tenantId).UpdateOneAsync(
            Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.ItemId,
                    subscriptionId)),
            Builders<SubscriptionDetail>.Update.PullFilter(
                subscription => subscription.PendingDocumentSources,
                source => source.SourceKey == sourceKey),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> RecordDocumentSourceFailureAsync(
        string tenantId,
        string subscriptionId,
        string sourceKey,
        string errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(errorCode);

        var result = await Subscriptions(tenantId).UpdateOneAsync(
            Builders<SubscriptionDetail>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionDetail>.Filter.Eq(
                    subscription => subscription.ItemId,
                    subscriptionId),
                Builders<SubscriptionDetail>.Filter.ElemMatch(
                    subscription => subscription.PendingDocumentSources,
                    source => source.SourceKey == sourceKey)),
            Builders<SubscriptionDetail>.Update
                .Inc("PendingDocumentSources.$[src].AttemptCount", 1)
                .Set("PendingDocumentSources.$[src].LastError", Shorten(errorCode)),
            new UpdateOptions
            {
                ArrayFilters =
                [
                    new BsonDocumentArrayFilterDefinition<SubscriptionDocumentSource>(
                        new BsonDocument("src.SourceKey", sourceKey))
                ]
            },
            cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<SubscriptionDetail>> ListTrialsStartedSinceAsync(
        string tenantId,
        DateTime sinceUtc,
        string? afterId,
        int limit,
        CancellationToken cancellationToken)
    {
        // A keyset page over (trial start, id). An instant alone cannot name a position: trials begin
        // in batches, and a page that filled up would be re-read forever or stepped over.
        var position = afterId is { Length: > 0 }
            ? Builders<SubscriptionDetail>.Filter.Or(
                Builders<SubscriptionDetail>.Filter.Gt("Trial.StartsAtUtc", sinceUtc),
                Builders<SubscriptionDetail>.Filter.And(
                    Builders<SubscriptionDetail>.Filter.Eq("Trial.StartsAtUtc", sinceUtc),
                    Builders<SubscriptionDetail>.Filter.Gt(
                        subscription => subscription.ItemId,
                        afterId)))
            : Builders<SubscriptionDetail>.Filter.Gte("Trial.StartsAtUtc", sinceUtc);

        var filter = Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Ne(subscription => subscription.Trial, null),
            position);

        return await Subscriptions(tenantId)
            .Find(filter)
            .Sort(Builders<SubscriptionDetail>.Sort
                .Ascending("Trial.StartsAtUtc")
                .Ascending(subscription => subscription.ItemId))
            .Limit(Math.Max(1, limit))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDetail>> ListWithPendingDocumentSourcesAsync(
        string tenantId,
        int maximumAttempts,
        int limit,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, maximumAttempts);

        var filter = Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.ElemMatch(
                subscription => subscription.PendingDocumentSources,
                source => source.AttemptCount < attempts));

        // Oldest first, so a backlog drains in the order the events happened rather than letting a
        // busy subscription's newest obligation jump one that has been waiting since the outage.
        return await Subscriptions(tenantId)
            .Find(filter)
            .SortBy(subscription => subscription.LastUpdatedDateUtc)
            .Limit(Math.Max(1, limit))
            .ToListAsync(cancellationToken);
    }

    private static FilterDefinition<SubscriptionDetail> NoSourceFor(string sourceKey) =>
        Builders<SubscriptionDetail>.Filter.Not(
            Builders<SubscriptionDetail>.Filter.ElemMatch(
                subscription => subscription.PendingDocumentSources,
                source => source.SourceKey == sourceKey));

    private static FilterDefinition<SubscriptionDetail> LeasedEventFilter(
        string tenantId,
        string subscriptionId,
        string eventId,
        string leaseId) =>
        Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.ItemId,
                subscriptionId),
            Builders<SubscriptionDetail>.Filter.ElemMatch(
                subscription => subscription.OutboxEvents,
                outboxEvent =>
                    outboxEvent.EventId == eventId &&
                    outboxEvent.LeaseId == leaseId));

    private static FilterDefinition<SubscriptionDetail> NotAlreadyEmitted(
        string deduplicationKey) =>
        Builders<SubscriptionDetail>.Filter.Not(
            Builders<SubscriptionDetail>.Filter.ElemMatch(
                subscription => subscription.OutboxEvents,
                outboxEvent => outboxEvent.DeduplicationKey == deduplicationKey));

    private static FilterDefinition<SubscriptionDetail> TenantFilter(string tenantId) =>
        Builders<SubscriptionDetail>.Filter.Eq(
            subscription => subscription.TenantId,
            tenantId);

    private static List<ArrayFilterDefinition> EventArrayFilter(string eventId) =>
    [
        new BsonDocumentArrayFilterDefinition<SubscriptionOutboxEvent>(
            new BsonDocument("evt.EventId", eventId))
    ];

    private static string Shorten(string value) =>
        value.Length <= 500 ? value : value[..500];

    private IMongoCollection<SubscriptionDetail> Subscriptions(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionDetail>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.Subscriptions);
}
