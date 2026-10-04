using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The reservation index once it knows the difference between an organization and a seat.
/// </summary>
/// <remarks>
/// This is the change that touches a guarantee live customers already rely on, so both halves are
/// asserted: an organization still cannot open a second subscription for itself, and it can now
/// hold user-wise ones alongside it.
/// <para>
/// The narrowed filter asks for an organization-wise scope with <c>$eq</c>, because a partial filter
/// may not use <c>$ne</c>, and <c>$eq</c> does not match a document that lacks the field. Every
/// subscription in production lacks it today. Built before the backfill, the index would cover none
/// of them, and the duplicate-signup guarantee would disappear without anything failing — which is
/// what <see cref="An_organization_written_before_the_scope_existed_still_cannot_subscribe_twice"/>
/// exists to catch.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class OrganizationScopedReservationIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionRepository _subscriptions;

    public OrganizationScopedReservationIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task An_organization_still_cannot_open_a_second_subscription_for_itself()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _subscriptions.TryCreateAsync(
            Organization(tenantId, "org-a", SubscriptionStatus.Active), CancellationToken.None))
            .Should().BeTrue();

        (await _subscriptions.TryCreateAsync(
                Organization(tenantId, "org-a", SubscriptionStatus.Incomplete),
                CancellationToken.None))
            .Should().BeFalse(
                because: "this is what live customers rely on, and narrowing the index by scope " +
                         "must not weaken it — the incomplete row still reserves checkout so " +
                         "nobody pays before discovering they are already subscribed");
    }

    [Fact]
    public async Task An_organization_written_before_the_scope_existed_still_cannot_subscribe_twice()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await InsertWithoutScopeAsync(tenantId, "org-legacy");

        // The backfill runs here, before the narrowed index is built. Were the order the other way
        // round the index would cover neither row and this second signup would be admitted.
        (await _subscriptions.TryCreateAsync(
                Organization(tenantId, "org-legacy", SubscriptionStatus.Incomplete),
                CancellationToken.None))
            .Should().BeFalse(
                because: "every subscription in production lacks the scope field, so if the " +
                         "backfill did not precede the index every existing customer would be " +
                         "free to subscribe twice and be charged twice");
    }

    [Fact]
    public async Task An_organization_may_hold_user_wise_subscriptions_beside_its_own()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _subscriptions.TryCreateAsync(
            Organization(tenantId, "org-mixed", SubscriptionStatus.Active), CancellationToken.None))
            .Should().BeTrue();

        (await _subscriptions.TryCreateAsync(
                UserWise(tenantId, "org-mixed", SubscriptionStatus.Active), CancellationToken.None))
            .Should().BeTrue(
                because: "the two cover different things — what the organization shares, and one " +
                         "person's own allowance — so buying a seat must not be refused as a " +
                         "duplicate of the organization's own plan");
    }

    [Fact]
    public async Task Several_user_wise_subscriptions_fit_in_one_organization()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _subscriptions.TryCreateAsync(
            UserWise(tenantId, "org-many", SubscriptionStatus.Active), CancellationToken.None))
            .Should().BeTrue();

        (await _subscriptions.TryCreateAsync(
                UserWise(tenantId, "org-many", SubscriptionStatus.Active), CancellationToken.None))
            .Should().BeTrue(
                because: "an organization buys one of these per person, and capping it at one " +
                         "would make the feature unsellable to its second employee");
    }

    [Fact]
    public async Task The_index_that_capped_an_organization_regardless_of_scope_is_gone()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);

        var collection = _fixture.DbContextProvider
            .GetDatabase(tenantId)
            .GetCollection<SubscriptionDetail>("Subscriptions");

        using var cursor = await collection.Indexes.ListAsync(CancellationToken.None);
        var names = (await cursor.ToListAsync(CancellationToken.None))
            .Select(index => index["name"].AsString)
            .ToList();

        names.Should().Contain(SubscriptionIndexDefinitions.SubscriptionReservationIndexName,
            because: "something has to keep enforcing one subscription per organization");
        names.Should().NotContain(
            SubscriptionIndexDefinitions.SubscriptionReservationLegacyIndexName,
            because: "it spans every subscription regardless of scope, so while it stands no " +
                     "organization can hold a seat-based plan beside its own");
    }

    /// <summary>
    /// Inserts a subscription whose plan snapshot has no scope, as every stored one does today.
    /// </summary>
    /// <remarks>
    /// Straight to the collection rather than through the repository, which would run the backfill
    /// on the way past and remove the very condition being tested.
    /// </remarks>
    private async Task InsertWithoutScopeAsync(string tenantId, string organizationId)
    {
        var document = Organization(tenantId, organizationId, SubscriptionStatus.Active)
            .ToBsonDocument();

        document["Plan"].AsBsonDocument.Remove(nameof(PlanSnapshot.SubscriberScope));

        await _fixture.Collection<BsonDocument>("Subscriptions")
            .InsertOneAsync(document, cancellationToken: CancellationToken.None);
    }

    private static SubscriptionDetail Organization(
        string tenantId,
        string organizationId,
        SubscriptionStatus status) =>
        NewSubscription(tenantId, organizationId, status, SubscriberScope.Organization);

    private static SubscriptionDetail UserWise(
        string tenantId,
        string organizationId,
        SubscriptionStatus status) =>
        NewSubscription(tenantId, organizationId, status, SubscriberScope.User);

    private static SubscriptionDetail NewSubscription(
        string tenantId,
        string organizationId,
        SubscriptionStatus status,
        SubscriberScope scope) => new()
        {
            TenantId = tenantId,
            OrganizationId = organizationId,
            Status = status,
            CurrencyCode = "CHF",
            Plan = new PlanSnapshot { Code = "professional", SubscriberScope = scope }
        };
}
