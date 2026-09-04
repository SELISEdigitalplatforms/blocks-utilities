using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Subscription;

public sealed class SubscriptionIndexDefinitionsTests
{
    [Fact]
    public void Signup_reservation_includes_incomplete_before_checkout()
    {
        var index = SubscriptionIndexDefinitions.CreateSubscriptionIndexes()
            .Single(candidate =>
                candidate.Options.Name ==
                SubscriptionIndexDefinitions.SubscriptionReservationIndexName);

        index.Options.Unique.Should().BeTrue();

        var partial = index.Options.PartialFilterExpression
            .Should()
            .BeOfType<BsonDocumentFilterDefinition<SubscriptionDetail>>()
            .Subject
            .Document;

        var reservedStatuses = partial[nameof(SubscriptionDetail.Status)]["$in"]
            .AsBsonArray
            .Select(value => value.AsInt32)
            .ToArray();

        reservedStatuses.Should().BeEquivalentTo(
        [
            (int)SubscriptionStatus.Incomplete,
            (int)SubscriptionStatus.Trialing,
            (int)SubscriptionStatus.Active,
            (int)SubscriptionStatus.PastDue
        ]);
        reservedStatuses.Should().NotContain((int)SubscriptionStatus.IncompleteExpired);
        reservedStatuses.Should().NotContain((int)SubscriptionStatus.Unpaid);
        reservedStatuses.Should().NotContain((int)SubscriptionStatus.Canceled);
    }

    [Fact]
    public void Usage_record_indexes_include_the_rollup_scan_index()
    {
        var index = SubscriptionIndexDefinitions.CreateUsageRecordIndexes()
            .Single(candidate =>
                candidate.Options.Name ==
                SubscriptionIndexDefinitions.UsageRecordRolloverScanIndexName);

        var keys = index.Keys.Render(
            new RenderArgs<SubscriptionUsageRecord>(
                BsonSerializer.SerializerRegistry.GetSerializer<SubscriptionUsageRecord>(),
                BsonSerializer.SerializerRegistry));

        keys.Should().BeEquivalentTo(new BsonDocument
        {
            { nameof(SubscriptionUsageRecord.TenantId), 1 },
            { nameof(SubscriptionUsageRecord.RecordedAtUtc), 1 },
            { "_id", 1 }
        });
    }

    [Fact]
    public void Usage_activity_rollup_identity_index_is_unique()
    {
        var index = SubscriptionIndexDefinitions.CreateUsageActivityRollupIndexes()
            .Single(candidate =>
                candidate.Options.Name ==
                SubscriptionIndexDefinitions.UsageActivityRollupUniqueIndexName);

        index.Options.Unique.Should().BeTrue();
    }

    [Fact]
    public void Usage_actor_rollup_identity_index_is_unique()
    {
        var index = SubscriptionIndexDefinitions.CreateUsageActorRollupIndexes()
            .Single(candidate =>
                candidate.Options.Name ==
                SubscriptionIndexDefinitions.UsageActorRollupUniqueIndexName);

        index.Options.Unique.Should().BeTrue();
    }

    [Fact]
    public void Usage_invoice_organization_index_exists_for_the_allowance_report()
    {
        SubscriptionIndexDefinitions.CreateUsageInvoiceIndexes()
            .Should().Contain(candidate =>
                candidate.Options.Name ==
                SubscriptionIndexDefinitions.UsageInvoiceOrganizationIndexName);
    }
}
