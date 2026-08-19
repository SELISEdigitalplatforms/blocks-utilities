using FluentAssertions;
using Payment.DomainService.Repositories;

namespace XUnitTest.Payment;

public sealed class PaymentQueryIndexDefinitionTests
{
    [Fact]
    public void Payment_query_indexes_are_part_of_lazy_tenant_indexes()
    {
        var names = PaymentIndexDefinitions.Create()
            .Select(index => index.Options.Name)
            .ToArray();

        names.Should().Contain(
        [
            PaymentIndexDefinitions.PaymentQueryDateIndexName,
            PaymentIndexDefinitions.PaymentQueryProviderIndexName,
            PaymentIndexDefinitions.PaymentQueryAmountIndexName,
            PaymentIndexDefinitions.PaymentQueryStatusIndexName,
            PaymentIndexDefinitions.SubscriptionInvoiceHistoryIndexName
        ]);
    }
}
