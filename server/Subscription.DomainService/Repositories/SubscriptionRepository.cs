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
    /// Statuses that grant something. The set the live-subscription index is partial on, and the
    /// one entitlement treats as current.
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
        CancellationToken cancellationToken) =>
        await Subscriptions(tenantId)
            .Find(BuildLiveFilter(tenantId, organizationId))
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
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> newQuantityItems,
        long newCreditBalanceMinor,
        string? planChangePaymentDetailId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newPlan);
        ArgumentNullException.ThrowIfNull(newPrice);
        ArgumentNullException.ThrowIfNull(newQuantityItems);
        ArgumentNullException.ThrowIfNull(outboxEvent);

        var filter = Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.ItemId,
                subscriptionId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.Version,
                expectedVersion));

        var update = Builders<SubscriptionDetail>.Update
            .Set(subscription => subscription.Plan, newPlan)
            .Set(subscription => subscription.Price, newPrice)
            .Set(subscription => subscription.QuantityItems, newQuantityItems)
            .Set(subscription => subscription.CreditBalanceMinor, newCreditBalanceMinor)
            .Inc(subscription => subscription.Version, 1)
            .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow)
            .Push(subscription => subscription.OutboxEvents, outboxEvent);

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

    public async Task<IReadOnlyList<SubscriptionDetail>> ListDueForUsageRatingAsync(
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
                // Explicitly excludes null rather than relying on how $lte treats it — same
                // reasoning as the renewal due-query: an immediately-canceled subscription
                // clears this field on purpose.
                Builders<SubscriptionDetail>.Filter.Ne(
                    subscription => subscription.NextUsageBillingAtUtc,
                    null),
                Builders<SubscriptionDetail>.Filter.Lte(
                    subscription => subscription.NextUsageBillingAtUtc,
                    asOfUtc)))
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
    public static FilterDefinition<SubscriptionDetail> BuildLiveFilter(
        string tenantId,
        string organizationId) =>
        Builders<SubscriptionDetail>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.OrganizationId,
                organizationId),
            Builders<SubscriptionDetail>.Filter.In(
                subscription => subscription.Status,
                LiveStatuses));

    private static UpdateDefinition<SubscriptionDetail> BuildTransitionUpdate(
        SubscriptionTransition transition)
    {
        var update = Builders<SubscriptionDetail>.Update
            .Set(subscription => subscription.Status, transition.NewStatus)
            .Set(subscription => subscription.LastUpdatedDateUtc, DateTime.UtcNow)
            .Inc(subscription => subscription.Version, 1);

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
