using FluentAssertions;
using Moq;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class HostedCheckoutClientResolverTests
{
    [Fact]
    public void Session_resolver_returns_the_client_that_supports_the_provider()
    {
        var adyen = Session(PaymentConstants.AdyenOnlineProvider);
        var other = Session("OTHER");
        var resolver = new PaymentSessionClientResolver([other, adyen]);

        resolver.Resolve(PaymentConstants.AdyenOnlineProvider).Should().BeSameAs(adyen);
        resolver.Resolve("OTHER").Should().BeSameAs(other);
    }

    [Fact]
    public void Session_resolver_returns_null_for_an_unserved_provider() =>
        new PaymentSessionClientResolver([Session(PaymentConstants.AdyenOnlineProvider)])
            .Resolve("STRIPE")
            .Should().BeNull();

    [Fact]
    public void Session_resolver_tolerates_having_no_clients_registered() =>
        new PaymentSessionClientResolver([])
            .Resolve(PaymentConstants.AdyenOnlineProvider)
            .Should().BeNull();

    [Fact]
    public void Result_resolver_returns_the_client_that_supports_the_provider()
    {
        var adyen = Result(PaymentConstants.AdyenOnlineProvider);
        var other = Result("OTHER");
        var resolver = new CheckoutResultClientResolver([other, adyen]);

        resolver.Resolve(PaymentConstants.AdyenOnlineProvider).Should().BeSameAs(adyen);
        resolver.Resolve("OTHER").Should().BeSameAs(other);
    }

    [Fact]
    public void Result_resolver_returns_null_for_an_unserved_provider() =>
        new CheckoutResultClientResolver([Result(PaymentConstants.AdyenOnlineProvider)])
            .Resolve("STRIPE")
            .Should().BeNull();

    [Fact]
    public void Result_resolver_tolerates_having_no_clients_registered() =>
        new CheckoutResultClientResolver([])
            .Resolve(PaymentConstants.AdyenOnlineProvider)
            .Should().BeNull();

    private static IPaymentSessionClient Session(string providerName)
    {
        var client = new Mock<IPaymentSessionClient>();
        client.Setup(x => x.Supports(providerName)).Returns(true);
        return client.Object;
    }

    private static ICheckoutResultClient Result(string providerName)
    {
        var client = new Mock<ICheckoutResultClient>();
        client.Setup(x => x.Supports(providerName)).Returns(true);
        return client.Object;
    }
}
