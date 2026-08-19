using FluentAssertions;
using MongoDB.Bson;
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
}
