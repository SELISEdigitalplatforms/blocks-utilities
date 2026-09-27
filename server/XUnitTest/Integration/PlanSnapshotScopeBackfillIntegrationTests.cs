using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The backfill that has to finish before the reservation index can name a scope.
/// </summary>
/// <remarks>
/// Guards two separate ways of breaking customers who are already paying.
/// <para>
/// The first is a duplicate subscription. A partial filter on
/// <see cref="SubscriberScope.Organization"/> does not match a document that lacks the field, so an
/// unmigrated subscription would fall out of the narrowed index and its organization could open a
/// second one. Only the stored document shows whether that is still possible — the entity's own
/// default reads as <c>Organization</c> either way — so these assert on raw BSON.
/// </para>
/// <para>
/// The second is quieter and worse. <c>Version</c> is what
/// <c>TryChangePlanAsync</c> and <c>TryApplyQuantityChangeAsync</c> compare-and-set against. A
/// backfill that moved it would make every plan and quantity change in flight when it ran fail its
/// guard and report a conflict the caller did nothing to cause — on first touch of every tenant in
/// every process, so on every deploy and every restart.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class PlanSnapshotScopeBackfillIntegrationTests
{
    private const string ScopeField = "Plan." + nameof(PlanSnapshot.SubscriberScope);

    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionRepository _subscriptions;

    public PlanSnapshotScopeBackfillIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task A_subscription_saved_before_the_scope_existed_is_recorded_as_organization_wise()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var itemId = await InsertWithoutScopeAsync(tenantId);

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);

        var plan = (await StoredAsync(tenantId, itemId))["Plan"].AsBsonDocument;

        plan.Contains(nameof(PlanSnapshot.SubscriberScope)).Should().BeTrue(
            because: "a partial filter naming the scope cannot match a document that lacks it, " +
                     "and such a subscription would leave its organization free to open a second");
        plan[nameof(PlanSnapshot.SubscriberScope)].AsInt32
            .Should().Be((int)SubscriberScope.Organization,
                because: "no other kind of plan has ever been sold, so this records what these " +
                         "subscriptions already are rather than deciding anything");
    }

    [Fact]
    public async Task The_backfill_does_not_move_the_version_a_concurrent_change_is_guarded_by()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var itemId = await InsertWithoutScopeAsync(tenantId, version: 7);

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);

        (await StoredAsync(tenantId, itemId))[nameof(SubscriptionDetail.Version)].AsInt32
            .Should().Be(7,
                because: "plan and quantity changes compare-and-set on this, so moving it would " +
                         "fail every one in flight with a conflict the caller never caused");
    }

    [Fact]
    public async Task The_backfill_does_not_restamp_when_the_subscription_last_changed()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var stamped = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var itemId = await InsertWithoutScopeAsync(tenantId, lastUpdatedUtc: stamped);

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);

        (await StoredAsync(tenantId, itemId))[nameof(SubscriptionDetail.LastUpdatedDateUtc)]
            .ToUniversalTime().Should().Be(stamped,
                because: "this is a migration, not a change to the subscription — reporting it " +
                         "as one would make every subscription look edited on the day we deployed");
    }

    [Fact]
    public async Task A_scope_already_recorded_is_left_alone()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var subscription = NewSubscription(tenantId);
        subscription.Plan.SubscriberScope = SubscriberScope.User;
        await Raw(tenantId).InsertOneAsync(
            subscription, cancellationToken: CancellationToken.None);

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);

        var plan = (await StoredAsync(tenantId, subscription.ItemId))["Plan"].AsBsonDocument;

        plan[nameof(PlanSnapshot.SubscriberScope)].AsInt32
            .Should().Be((int)SubscriberScope.User,
                because: "the backfill fills in what is missing; overwriting a recorded scope " +
                         "would move a user-wise subscription onto the organization's own slot");
    }

    [Fact]
    public async Task Running_it_again_changes_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var itemId = await InsertWithoutScopeAsync(tenantId, version: 7);

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);
        await new SubscriptionRepository(_fixture.DbContextProvider)
            .EnsureIndexesAsync(tenantId, CancellationToken.None);

        var stored = await StoredAsync(tenantId, itemId);

        stored[nameof(SubscriptionDetail.Version)].AsInt32.Should().Be(7,
            because: "every host runs this on its first touch of a tenant, so a second pass must " +
                     "be a no-op rather than another write");
        stored["Plan"].AsBsonDocument[nameof(PlanSnapshot.SubscriberScope)].AsInt32
            .Should().Be((int)SubscriberScope.Organization);
    }

    /// <summary>
    /// The organization's own subscription is still found when its scope was never written.
    /// </summary>
    /// <remarks>
    /// This is the live-customer risk in narrowing those lookups. Every subscription in production
    /// today lacks the field, and an <c>$eq</c> on Organization would match none of them -- every
    /// organization would appear to have no subscription at all, losing entitlement and blocking
    /// renewal. The filter says "not user-wise" instead, because a query's <c>$ne</c> matches a
    /// document where the field is absent.
    /// </remarks>
    [Fact]
    public async Task An_unmigrated_subscription_is_still_the_organizations_own()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId);
        var document = subscription.ToBsonDocument();

        document["Plan"].AsBsonDocument.Remove(nameof(PlanSnapshot.SubscriberScope));

        await _fixture.Collection<BsonDocument>("Subscriptions")
            .InsertOneAsync(document, cancellationToken: CancellationToken.None);

        // Read before EnsureIndexesAsync has a chance to backfill, which is the state every live
        // subscription is in the moment this ships.
        (await _subscriptions.GetLiveAsync(
                tenantId,
                subscription.OrganizationId,
                DateTime.UtcNow,
                CancellationToken.None))
            .Should().NotBeNull(
                because: "an organization that could not find its own subscription would lose " +
                         "every entitlement it pays for, and its renewal with them");
    }

    /// <remarks>
    /// The other half of the same filter: a seat-based subscription must not be mistaken for the
    /// organization's, or the first user to buy one would displace what the organization shares.
    /// </remarks>
    [Fact]
    public async Task A_user_wise_subscription_is_never_taken_for_the_organizations_own()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId);
        subscription.Plan.SubscriberScope = SubscriberScope.User;

        await Raw(tenantId).InsertOneAsync(
            subscription, cancellationToken: CancellationToken.None);

        (await _subscriptions.GetLiveAsync(
                tenantId,
                subscription.OrganizationId,
                DateTime.UtcNow,
                CancellationToken.None))
            .Should().BeNull(
                because: "it belongs to whoever holds its seat, and answering with it would hand " +
                         "one person's plan to everyone in the organization");
    }

    /// <summary>
    /// Inserts a subscription whose plan snapshot has no scope at all, as every stored one does.
    /// </summary>
    /// <remarks>
    /// Written as raw BSON with the field removed, because the entity cannot express its own
    /// absence: serializing it always emits <c>Organization</c>, which is the migrated state rather
    /// than the one every existing document is actually in.
    /// </remarks>
    private async Task<string> InsertWithoutScopeAsync(
        string tenantId,
        int version = 1,
        DateTime? lastUpdatedUtc = null)
    {
        var subscription = NewSubscription(tenantId);
        subscription.Version = version;
        subscription.LastUpdatedDateUtc = lastUpdatedUtc ?? subscription.LastUpdatedDateUtc;

        var document = subscription.ToBsonDocument();
        document["Plan"].AsBsonDocument.Remove(nameof(PlanSnapshot.SubscriberScope));

        await _fixture.Collection<BsonDocument>("Subscriptions")
            .InsertOneAsync(document, cancellationToken: CancellationToken.None);

        return subscription.ItemId;
    }

    private async Task<BsonDocument> StoredAsync(string tenantId, string itemId) =>
        await _fixture.DbContextProvider
            .GetDatabase(tenantId)
            .GetCollection<BsonDocument>("Subscriptions")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", itemId))
            .FirstAsync(CancellationToken.None);

    private IMongoCollection<SubscriptionDetail> Raw(string tenantId) =>
        _fixture.DbContextProvider
            .GetDatabase(tenantId)
            .GetCollection<SubscriptionDetail>("Subscriptions");

    private static SubscriptionDetail NewSubscription(string tenantId) => new()
    {
        TenantId = tenantId,
        OrganizationId = "org-" + Guid.NewGuid().ToString("N"),
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot { Code = "professional" }
    };
}
