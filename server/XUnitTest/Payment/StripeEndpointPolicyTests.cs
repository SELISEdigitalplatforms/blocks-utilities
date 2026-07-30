using FluentAssertions;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StripeEndpointPolicyTests
{
    private readonly StripeEndpointPolicy _policy = new();

    [Theory]
    [InlineData("STRIPE")]
    [InlineData("stripe")]
    public void Supports_only_stripe(string providerName) =>
        _policy.Supports(providerName).Should().BeTrue();

    [Fact]
    public void Does_not_claim_another_provider() =>
        _policy.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();

    [Theory]
    [InlineData("https://api.stripe.com")]
    [InlineData("https://api.stripe.com/v1")]
    public void Accepts_the_stripe_api_host(string url) =>
        _policy.IsAllowed(url).Should().BeTrue();

    [Theory]
    [InlineData("http://api.stripe.com/v1")]
    [InlineData("https://api.stripe.com.evil.example/v1")]
    [InlineData("https://checkout-test.adyen.com/v72")]
    [InlineData("https://127.0.0.1/v1")]
    [InlineData("https://10.0.0.5/v1")]
    [InlineData("not-a-url")]
    [InlineData(null)]
    public void Rejects_anything_else(string? url) =>
        _policy.IsAllowed(url).Should().BeFalse();
}
