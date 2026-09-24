using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// That a signup reservation index left behind by an earlier rename is actually removed.
/// </summary>
/// <remarks>
/// Guards a charge with no subscription. A superseded index keyed on the organization alone goes
/// on enforcing one subscription per organization, and because its partial filter excludes
/// <see cref="SubscriptionStatus.Incomplete"/> the conflict is invisible at signup and lands on the
/// transition to <see cref="SubscriptionStatus.Active"/> -- after the customer has paid.
/// <para>
/// Only a real database can show this. The leftover is created by MongoDB and never appears in the
/// index definitions, so nothing in the source mentions it and a mocked collection would report the
/// migration as clean while the real one still held the index.
/// </para>
/// <para>
/// <c>ux_subscription_tenant_org_live</c> is the one that actually exists in the wild -- the
/// pre-<c>_v2</c> name, dropped from the source when the constant was replaced rather than kept so
/// it could be dropped from databases. The second, invented name stands in for the leftovers nobody
/// has enumerated yet, which is what forces the migration to match on shape instead of a name list.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionReservationIndexMigrationTests
{
    private const string LegacyLiveIndexName = "ux_subscription_tenant_org_live";
    private const string UnknownLegacyIndexName = "ux_subscription_tenant_org_something_older";

    private readonly MongoIntegrationFixture _fixture;

    public SubscriptionReservationIndexMigrationTests(MongoIntegrationFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task A_superseded_reservation_index_is_dropped_even_though_no_constant_names_it()
    {
        var collection = await SeedLegacyIndexesAsync(UnknownLegacyIndexName);

        await new SubscriptionRepository(_fixture.DbContextProvider)
            .EnsureIndexesAsync(MongoIntegrationFixture.NewTenantId(), CancellationToken.None);

        (await IndexNamesAsync(collection))
            .Should().NotContain(UnknownLegacyIndexName,
                because: "a name list only removes the leftovers we already know about, which is " +
                         "exactly how ux_subscription_tenant_org_live survived a rename unnoticed");
    }

    [Fact]
    public async Task The_pre_v2_reservation_index_does_not_survive_the_migration()
    {
        var collection = await SeedLegacyIndexesAsync(LegacyLiveIndexName);

        await new SubscriptionRepository(_fixture.DbContextProvider)
            .EnsureIndexesAsync(MongoIntegrationFixture.NewTenantId(), CancellationToken.None);

        (await IndexNamesAsync(collection))
            .Should().NotContain(LegacyLiveIndexName,
                because: "left in place it keeps capping an organization at one subscription, and " +
                         "its filter skips Incomplete so the customer is charged before it refuses");
    }

    [Fact]
    public async Task The_current_reservation_index_survives_the_migration()
    {
        var collection = await SeedLegacyIndexesAsync(LegacyLiveIndexName);

        await new SubscriptionRepository(_fixture.DbContextProvider)
            .EnsureIndexesAsync(MongoIntegrationFixture.NewTenantId(), CancellationToken.None);

        (await IndexNamesAsync(collection))
            .Should().Contain(SubscriptionIndexDefinitions.SubscriptionReservationIndexName,
                because: "it shares its key with what is being dropped, so a shape match that " +
                         "failed to exclude it would take the live duplicate-signup guard with it");
    }

    [Fact]
    public async Task The_organization_read_index_survives_the_migration()
    {
        var collection = await SeedLegacyIndexesAsync(LegacyLiveIndexName);

        await new SubscriptionRepository(_fixture.DbContextProvider)
            .EnsureIndexesAsync(MongoIntegrationFixture.NewTenantId(), CancellationToken.None);

        (await IndexNamesAsync(collection))
            .Should().Contain(SubscriptionIndexDefinitions.SubscriptionOrganizationIndexName,
                because: "it is not unique and enforces nothing, so dropping it would cost the " +
                         "renewal and listing queries their index without removing any hazard");
    }

    [Fact]
    public async Task An_organization_still_cannot_open_a_second_subscription_afterwards()
    {
        await SeedLegacyIndexesAsync(LegacyLiveIndexName);

        var repository = new SubscriptionRepository(_fixture.DbContextProvider);
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await repository.TryCreateAsync(
            NewSubscription(tenantId, SubscriptionStatus.Active), CancellationToken.None))
            .Should().BeTrue();

        (await repository.TryCreateAsync(
                NewSubscription(tenantId, SubscriptionStatus.Incomplete), CancellationToken.None))
            .Should().BeFalse(
                because: "the index being dropped only ever duplicated a rule the current one " +
                         "already enforces more strictly, so removing it must change nothing");
    }

    /// <summary>
    /// The subscriber-keyed index is dropped, not merely superseded.
    /// </summary>
    /// <remarks>
    /// Left in place it would refuse exactly what it was added to allow. Who holds a seat now lives
    /// in its own collection, so no subscription carries the field this was keyed on, every one of
    /// them indexes it as null, and two user-wise subscriptions in one organization collide on
    /// <c>{tenant, org, null}</c>.
    /// <para>
    /// It is dropped by name rather than by the shape sweep beside it, which matches two-field keys
    /// only -- this one has three.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_subscriber_keyed_reservation_index_does_not_survive_the_migration()
    {
        var collection = _fixture.Collection<SubscriptionDetail>("Subscriptions");

        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<SubscriptionDetail>(
                new BsonDocument
                {
                    { nameof(SubscriptionDetail.TenantId), 1 },
                    { nameof(SubscriptionDetail.OrganizationId), 1 },
                    { "SubscriberUserId", 1 }
                },
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = SubscriptionIndexDefinitions
                        .SubscriptionSubscriberReservationLegacyIndexName
                }),
            cancellationToken: CancellationToken.None);

        await new SubscriptionRepository(_fixture.DbContextProvider)
            .EnsureIndexesAsync(MongoIntegrationFixture.NewTenantId(), CancellationToken.None);

        (await IndexNamesAsync(collection))
            .Should().NotContain(
                SubscriptionIndexDefinitions.SubscriptionSubscriberReservationLegacyIndexName,
                because: "every subscription now indexes the removed field as null, so this would " +
                         "cap an organization at one user-wise subscription — the opposite of " +
                         "what it was created for");
    }

    /// <summary>
    /// Recreates a tenant database that predates the rename, then hands back the raw collection.
    /// </summary>
    /// <remarks>
    /// Built with the filter the real one carries -- the three granting statuses and not
    /// <see cref="SubscriptionStatus.Incomplete"/> -- because that gap is the whole reason a
    /// leftover is dangerous rather than merely redundant.
    /// </remarks>
    private async Task<IMongoCollection<SubscriptionDetail>> SeedLegacyIndexesAsync(string name)
    {
        var collection = _fixture.Collection<SubscriptionDetail>("Subscriptions");

        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<SubscriptionDetail>(
                Builders<SubscriptionDetail>.IndexKeys
                    .Ascending(subscription => subscription.TenantId)
                    .Ascending(subscription => subscription.OrganizationId),
                new CreateIndexOptions<SubscriptionDetail>
                {
                    Unique = true,
                    Name = name,
                    PartialFilterExpression = new BsonDocument(
                        nameof(SubscriptionDetail.Status),
                        new BsonDocument(
                            "$in",
                            new BsonArray
                            {
                                (int)SubscriptionStatus.Trialing,
                                (int)SubscriptionStatus.Active,
                                (int)SubscriptionStatus.PastDue
                            }))
                }),
            cancellationToken: CancellationToken.None);

        return collection;
    }

    private static async Task<IReadOnlyList<string>> IndexNamesAsync(
        IMongoCollection<SubscriptionDetail> collection)
    {
        using var cursor = await collection.Indexes.ListAsync(CancellationToken.None);
        var indexes = await cursor.ToListAsync(CancellationToken.None);

        return [.. indexes.Select(index => index["name"].AsString)];
    }

    private static SubscriptionDetail NewSubscription(
        string tenantId,
        SubscriptionStatus status) => new()
        {
            TenantId = tenantId,
            OrganizationId = "org-legacy-index",
            Status = status,
            CurrencyCode = "CHF"
        };
}
