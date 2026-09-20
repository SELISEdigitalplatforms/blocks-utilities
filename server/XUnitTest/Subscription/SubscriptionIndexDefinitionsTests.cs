using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Subscription;

public sealed class SubscriptionIndexDefinitionsTests
{
    /// <summary>
    /// Reporting's index shapes live in the sets the owning collections already create.
    /// </summary>
    /// <remarks>
    /// Declared there rather than in a set of their own so that the repository owning a
    /// collection and the reporting repository create the same index from one definition. Two
    /// definitions under one name is a disagreement that surfaces only in production, where
    /// whichever repository reached a tenant database first silently decides the key.
    /// </remarks>
    [Fact]
    public void The_reporting_indexes_belong_to_the_sets_their_collections_already_create()
    {
        SubscriptionIndexDefinitions.CreateUsageRecordIndexes()
            .Should().ContainSingle(candidate =>
                candidate.Options.Name ==
                SubscriptionIndexDefinitions.UsageRecordReportingIndexName);

        SubscriptionIndexDefinitions.CreateFinancialDocumentIndexes()
            .Should().ContainSingle(candidate =>
                candidate.Options.Name ==
                SubscriptionIndexDefinitions.FinancialDocumentReportingIndexName);
    }

    /// <summary>
    /// Two index models sharing a name inside one set is refused by the server, not by the
    /// compiler, and only on the first call that touches a fresh tenant database.
    /// </summary>
    [Theory]
    [MemberData(nameof(IndexSetNames))]
    public void Index_names_are_unique_within_their_set(
        string setName,
        IReadOnlyList<string> names) =>
        names.Should().OnlyHaveUniqueItems(
            "{0} would otherwise fail on the first tenant it is created for",
            setName);

    public static TheoryData<string, IReadOnlyList<string>> IndexSetNames() =>
        new()
        {
            {
                nameof(SubscriptionIndexDefinitions.CreateSubscriptionIndexes),
                Names(SubscriptionIndexDefinitions.CreateSubscriptionIndexes())
            },
            {
                nameof(SubscriptionIndexDefinitions.CreateUsageRecordIndexes),
                Names(SubscriptionIndexDefinitions.CreateUsageRecordIndexes())
            },
            {
                nameof(SubscriptionIndexDefinitions.CreateFinancialDocumentIndexes),
                Names(SubscriptionIndexDefinitions.CreateFinancialDocumentIndexes())
            },
            {
                nameof(SubscriptionIndexDefinitions.CreateUsageCurrentIndexes),
                Names(SubscriptionIndexDefinitions.CreateUsageCurrentIndexes())
            }
        };

    private static IReadOnlyList<string> Names<TDocument>(
        IEnumerable<CreateIndexModel<TDocument>> indexes) =>
        [.. indexes.Select(index => index.Options.Name ?? string.Empty)];

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
}
