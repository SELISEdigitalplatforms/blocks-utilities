using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Subscription;

public sealed class SubscriptionInvoiceHistoryFilterTests
{
    [Fact]
    public void Invoice_history_is_scoped_to_the_subscriber_not_the_merchant()
    {
        var rendered = Render();
        var text = rendered.ToString();

        text.Should().Contain("CustomerOrganizationId");
        text.Should().Contain("subscriber-1");
        text.Should().NotContain("\"OrganizationId\"");
        text.Should().Contain("SUBSCRIPTION_INVOICE");
        text.Should().Contain("ProviderInvoiceId");
        text.Should().Contain("CAPTURED");
        text.Should().Contain("PARTIALLY_REFUNDED");
        text.Should().Contain("REFUNDED");
    }

    [Fact]
    public void Cursor_selects_only_records_after_the_descending_boundary()
    {
        var rendered = Render(new SubscriptionInvoiceHistoryCursor(
            new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
            "payment-9"));
        var text = rendered.ToString();

        text.Should().Contain("$lt");
        text.Should().Contain("PaymentDate");
        text.Should().Contain("payment-9");
    }

    private static BsonDocument Render(SubscriptionInvoiceHistoryCursor? cursor = null)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<PaymentDetail>();
        return SubscriptionInvoiceHistoryRepository
            .BuildFilter("tenant-1", "subscriber-1", cursor)
            .Render(new RenderArgs<PaymentDetail>(
                serializer,
                BsonSerializer.SerializerRegistry));
    }
}
