using Blocks.Genesis;
using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentTenantContextScopeFactoryTests
{
    [Fact]
    public void Establish_sets_and_restores_the_payment_tenant_context()
    {
        const string previousTenant = "previous-tenant";
        const string paymentTenant = "payment-tenant";
        var previousContext = CreateContext(previousTenant);
        BlocksContext.SetContext(previousContext);
        var factory = new PaymentTenantContextScopeFactory();

        using (factory.Establish(paymentTenant))
        {
            var activeContext = BlocksContext.GetContext();

            activeContext.Should().NotBeNull();
            activeContext!.TenantId.Should().Be(paymentTenant);
            activeContext.OriginalTenantId.Should().Be(paymentTenant);
        }

        var restoredContext = BlocksContext.GetContext();

        restoredContext.Should().BeSameAs(previousContext);
        BlocksContext.ClearContext();
    }

    [Fact]
    public void Establish_rejects_an_empty_tenant_identifier()
    {
        var factory = new PaymentTenantContextScopeFactory();

        var action = () => factory.Establish(" ");

        action.Should().Throw<ArgumentException>();
    }

    private static BlocksContext CreateContext(string tenantId) =>
        BlocksContext.Create(
            tenantId,
            Array.Empty<string>(),
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            DateTime.MinValue,
            string.Empty,
            Array.Empty<string>(),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            tenantId,
            string.Empty);
}
