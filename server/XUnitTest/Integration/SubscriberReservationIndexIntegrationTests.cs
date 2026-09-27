using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The groundwork a user-wise subscription needs, and the guarantee that none of it is reachable yet.
/// </summary>
/// <remarks>
/// Guards two customer-visible failures at once. The first is a duplicate charge: an absent
/// <see cref="SubscriptionDetail.SubscriberUserId"/> indexes as null while a written one indexes as
/// the empty string, so an organization with a subscription predating the field could open a second
/// one the moment the organization-keyed index is dropped. The second is a regression in today's
/// behaviour, which has to stay exactly as it was while this field is inert.
/// <para>
/// Only a real database shows either. Both turn on how MongoDB keys a missing field and on which of
/// two overlapping unique indexes rejects a write first — neither of which a stand-in reproduces.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriberReservationIndexIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionRepository _subscriptions;

    public SubscriberReservationIndexIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task A_document_written_before_the_field_existed_is_given_the_organization_wide_value()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var itemId = await InsertPreFieldSubscriptionAsync(tenantId, SubscriptionStatus.Active);

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);

        var stored = await StoredAsync(tenantId, itemId);

        stored.Contains(nameof(SubscriptionDetail.SubscriberUserId)).Should().BeTrue(
            because: "the index keys on the stored field, and an absent one keys as null rather " +
                     "than as the empty string a migrated document carries");
        stored[nameof(SubscriptionDetail.SubscriberUserId)].AsString.Should().BeEmpty(
            because: "empty is not a placeholder — it is the organization-wide subscriber these " +
                     "documents have always been");
    }

    [Fact]
    public async Task The_backfill_leaves_a_subscriber_already_recorded_alone()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var subscription = NewSubscription(tenantId, "org-keep", SubscriptionStatus.Active);
        subscription.SubscriberUserId = "user-keep";
        await Raw(tenantId).InsertOneAsync(subscription, cancellationToken: CancellationToken.None);

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);

        var after = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);

        after!.SubscriberUserId.Should().Be("user-keep",
            because: "the backfill fills in what is missing; overwriting a recorded subscriber " +
                     "would silently move their subscription to the organization");
    }

    [Fact]
    public async Task Running_the_backfill_again_changes_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var itemId = await InsertPreFieldSubscriptionAsync(tenantId, SubscriptionStatus.Active);

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);
        await new SubscriptionRepository(_fixture.DbContextProvider)
            .EnsureIndexesAsync(tenantId, CancellationToken.None);

        var stored = await StoredAsync(tenantId, itemId);

        stored[nameof(SubscriptionDetail.SubscriberUserId)].AsString.Should().BeEmpty(
            because: "every host runs this on its first touch of a tenant, so it has to be safe " +
                     "to run repeatedly rather than once");
    }

    /// <summary>
    /// The hazard the backfill exists for, staged as the state that follows dropping the narrower index.
    /// </summary>
    /// <remarks>
    /// The organization-keyed index has to be taken out of the way to see this at all: while it
    /// stands it refuses the second write on its own, so the test would pass whether the documents
    /// were migrated or not. What is left is the subscriber-keyed index alone -- the arrangement the
    /// next change ships -- where an unmigrated row keys on null, a new one on the empty string, and
    /// the organization quietly ends up paying for two subscriptions.
    /// </remarks>
    [Fact]
    public async Task A_backfilled_document_collides_under_the_subscriber_key_alone()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await InsertPreFieldSubscriptionAsync(tenantId, SubscriptionStatus.Active, "org-collide");

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);

        bool inserted;

        try
        {
            await Raw(tenantId).Indexes.DropOneAsync(
                SubscriptionIndexDefinitions.SubscriptionReservationIndexName,
                CancellationToken.None);

            inserted = await TryInsertDirectlyAsync(
                tenantId, NewSubscription(tenantId, "org-collide", SubscriptionStatus.Incomplete));
        }
        finally
        {
            // The fixture puts every tenant in one collection, so the dropped index and anything
            // this admitted are visible to every other test. Both are undone here rather than left
            // to the next EnsureIndexesAsync: a second live row for one organization would make
            // rebuilding the unique index throw, and the test that failed would not be this one.
            await RestoreSharedCollectionAsync(tenantId);
        }

        inserted.Should().BeFalse(
            because: "an unmigrated row keys as null while this one keys as the empty string, so " +
                     "without the backfill the subscriber-keyed index admits both and the " +
                     "organization is charged for two subscriptions");
    }

    [Fact]
    public async Task The_subscriber_keyed_index_is_created_beside_the_organization_keyed_one()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _subscriptions.EnsureIndexesAsync(tenantId, CancellationToken.None);

        var names = await IndexNamesAsync(tenantId);

        names.Should().Contain(SubscriptionIndexDefinitions.SubscriptionSubscriberReservationIndexName)
            .And.Contain(SubscriptionIndexDefinitions.SubscriptionReservationIndexName,
                because: "the narrower index is what keeps user-wise subscriptions unreachable " +
                         "until every tenant is migrated, so this must add to it, not replace it");
    }

    [Fact]
    public async Task A_second_subscriber_in_one_organization_is_still_refused_for_now()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = NewSubscription(tenantId, "org-shared", SubscriptionStatus.Active);
        first.SubscriberUserId = "user-a";
        (await _subscriptions.TryCreateAsync(first, CancellationToken.None)).Should().BeTrue();

        var second = NewSubscription(tenantId, "org-shared", SubscriptionStatus.Incomplete);
        second.SubscriberUserId = "user-b";

        (await _subscriptions.TryCreateAsync(second, CancellationToken.None))
            .Should().BeFalse(
                because: "the organization-keyed index is still in force, so this change is " +
                         "groundwork only and cannot alter what can be sold today");
    }

    /// <summary>
    /// Inserts a subscription with no <see cref="SubscriptionDetail.SubscriberUserId"/> at all.
    /// </summary>
    /// <remarks>
    /// Written as raw BSON with the field removed, because the entity cannot express its own
    /// absence — serializing it always emits the empty string, which is the migrated state rather
    /// than the one every existing document is actually in.
    /// </remarks>
    private async Task<string> InsertPreFieldSubscriptionAsync(
        string tenantId,
        SubscriptionStatus status,
        string organizationId = "org-legacy")
    {
        var subscription = NewSubscription(tenantId, organizationId, status);
        var document = subscription.ToBsonDocument();

        document.Remove(nameof(SubscriptionDetail.SubscriberUserId));

        await _fixture.Collection<BsonDocument>("Subscriptions")
            .InsertOneAsync(document, cancellationToken: CancellationToken.None);

        return subscription.ItemId;
    }

    /// <summary>
    /// Puts the shared collection back as it was found: this tenant's rows gone, the indexes whole.
    /// </summary>
    /// <remarks>
    /// Deletes before recreating, because the index being restored is unique and a row this test
    /// admitted is exactly what would stop it being built again.
    /// </remarks>
    private async Task RestoreSharedCollectionAsync(string tenantId)
    {
        await Raw(tenantId).DeleteManyAsync(
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.TenantId, tenantId),
            CancellationToken.None);

        await Raw(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateSubscriptionIndexes(), CancellationToken.None);
    }

    /// <summary>
    /// Reads the document as it is actually stored, bypassing the entity's own defaults.
    /// </summary>
    /// <remarks>
    /// <see cref="SubscriptionDetail.SubscriberUserId"/> initialises to the empty string, so a
    /// document that never had the field deserializes identically to a migrated one. Only the raw
    /// document tells the two apart -- and it is the raw document the index keys on.
    /// </remarks>
    private async Task<BsonDocument> StoredAsync(string tenantId, string itemId) =>
        await _fixture.DbContextProvider
            .GetDatabase(tenantId)
            .GetCollection<BsonDocument>("Subscriptions")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", itemId))
            .FirstAsync(CancellationToken.None);

    /// <summary>
    /// Inserts without the repository, so a duplicate key is reported rather than swallowed.
    /// </summary>
    /// <remarks>
    /// <c>TryCreateAsync</c> catches a duplicate key and returns false, which is indistinguishable
    /// here from any other refusal; this keeps the test tied to the index doing the rejecting.
    /// </remarks>
    private async Task<bool> TryInsertDirectlyAsync(
        string tenantId,
        SubscriptionDetail subscription)
    {
        try
        {
            await Raw(tenantId).InsertOneAsync(
                subscription, cancellationToken: CancellationToken.None);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    private IMongoCollection<SubscriptionDetail> Raw(string tenantId) =>
        _fixture.DbContextProvider
            .GetDatabase(tenantId)
            .GetCollection<SubscriptionDetail>("Subscriptions");

    private async Task<IReadOnlyList<string>> IndexNamesAsync(string tenantId)
    {
        using var cursor = await Raw(tenantId).Indexes.ListAsync(CancellationToken.None);
        var indexes = await cursor.ToListAsync(CancellationToken.None);

        return [.. indexes.Select(index => index["name"].AsString)];
    }

    private static SubscriptionDetail NewSubscription(
        string tenantId,
        string organizationId,
        SubscriptionStatus status) => new()
        {
            TenantId = tenantId,
            OrganizationId = organizationId,
            Status = status,
            CurrencyCode = "CHF"
        };
}
