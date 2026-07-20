using FluentAssertions;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;

namespace XUnitTest.Payment;

public sealed class PaymentIndexDefinitionsTests
{
    [Fact]
    public void Outbox_deduplication_index_excludes_payments_without_a_deduplication_key()
    {
        var index = PaymentIndexDefinitions.Create().Single(
            definition => definition.Options.Name ==
                          PaymentIndexDefinitions.OutboxDeduplicationIndexName);
        var options = index.Options.Should()
            .BeOfType<CreateIndexOptions<PaymentDetail>>()
            .Subject;

        options.Unique.Should().BeTrue();
        options.Sparse.Should().NotBeTrue();
        options.PartialFilterExpression.Should().NotBeNull();
        options.Name.Should().NotBe(
            PaymentIndexDefinitions.LegacyOutboxDeduplicationIndexName);
    }

    [Fact]
    public void Recurring_order_index_is_unique_and_partial()
    {
        var index = PaymentIndexDefinitions.Create().Single(
            definition => definition.Options.Name ==
                          PaymentIndexDefinitions
                              .RecurringOrderIndexName);
        var options = index.Options.Should()
            .BeOfType<CreateIndexOptions<PaymentDetail>>()
            .Subject;

        options.Unique.Should().BeTrue();
        options.PartialFilterExpression.Should().NotBeNull();
    }
}
