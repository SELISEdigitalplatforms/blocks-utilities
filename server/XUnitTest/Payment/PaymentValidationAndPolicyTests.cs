using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Payment.DomainService.Validators;

namespace XUnitTest.Payment;

public sealed class PaymentValidationAndPolicyTests
{
    [Fact]
    public void Validator_rejects_unsupported_provider_and_invalid_amount()
    {
        var request = ValidRequest();
        request.ProviderName = "OTHER";
        request.Amount = 0;

        var result = CreateValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.PropertyName).Should().Contain(["ProviderName", "Amount"]);
    }

    [Fact]
    public void Validator_rejects_merchant_initiated_recurring_fields()
    {
        var request = ValidRequest();
        request.IsRecurring = true;
        request.RecurringModel = null;

        var result = CreateValidator().Validate(request);

        result.Errors.Select(error => error.PropertyName)
            .Should()
            .Contain(["IsRecurring"]);
    }

    [Fact]
    public void Validator_rejects_conflicting_save_preferences()
    {
        var request = ValidRequest();
        request.SavePaymentMethod = true;
        request.RememberCard = false;

        var result =
            CreateValidator().Validate(request);

        result.Errors.Should().ContainSingle(
            error =>
                error.PropertyName ==
                nameof(MakePaymentRequest.SavePaymentMethod));
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(null, true, true)]
    [InlineData(false, null, false)]
    [InlineData(null, null, false)]
    public void Save_preference_resolves_canonical_and_legacy_values(
        bool? savePaymentMethod,
        bool? rememberCard,
        bool expected)
    {
        var request = ValidRequest();
        request.SavePaymentMethod = savePaymentMethod;
        request.RememberCard = rememberCard;

        request.ShouldSavePaymentMethod.Should().Be(expected);
    }

    [Theory]
    [InlineData("USD", 10.25, 1025)]
    [InlineData("JPY", 10, 10)]
    [InlineData("BHD", 1.125, 1125)]
    public void Currency_resolver_converts_configured_precision(string currency, decimal amount, long expected)
    {
        var resolver = new CurrencyMinorUnitResolver(Monitor(new PaymentOptions()));

        resolver.TryConvert(amount, currency, out var minorUnits).Should().BeTrue();
        minorUnits.Should().Be(expected);
    }

    [Theory]
    [InlineData("USD", 1.001)]
    [InlineData("XYZ", 10)]
    [InlineData("JPY", 1.5)]
    public void Currency_resolver_rejects_unsupported_or_excess_precision(string currency, decimal amount)
    {
        var resolver = new CurrencyMinorUnitResolver(Monitor(new PaymentOptions()));

        resolver.TryConvert(amount, currency, out _).Should().BeFalse();
    }

    [Fact]
    public void Return_url_policy_uses_database_urls_and_adds_only_signed_state()
    {
        var provider = Provider();
        var policy = new CheckoutUrlPolicy();

        var allowed = policy.TryResolveHostedUrls(provider, "signed-state", out var resolved, out var frontend);

        allowed.Should().BeTrue();
        resolved.Should().Contain("state=signed-state").And.NotContain("PaymentDetailId");
        frontend.Should().Be("https://app.merchant.example/payment-result");
    }

    [Theory]
    [InlineData("http://checkout.merchant.example/complete")]
    [InlineData("https://127.0.0.1/complete")]
    public void Return_url_policy_rejects_unsafe_backend_urls(string url)
    {
        var provider = Provider();

        provider.ReturnUrl = url;
        new CheckoutUrlPolicy().TryResolveHostedUrls(provider, "state", out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://checkout-test.adyen.com/v72")]
    [InlineData("https://checkout-live.adyenpayments.com/checkout/v72")]
    public void Provider_policy_accepts_Adyen_https_endpoints(string url) =>
        new AdyenEndpointPolicy().IsAllowed(url).Should().BeTrue();

    [Theory]
    [InlineData("https://checkout-test.adyen.com/v71")]
    [InlineData("http://checkout-test.adyen.com/v72")]
    [InlineData("https://localhost/v72")]
    [InlineData("https://evil.example/v72")]
    public void Provider_policy_rejects_non_Adyen_or_unsafe_endpoints(string url) =>
        new AdyenEndpointPolicy().IsAllowed(url).Should().BeFalse();

    [Fact]
    public void Adyen_policy_only_claims_the_adyen_online_provider()
    {
        var policy = new AdyenEndpointPolicy();

        policy.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        policy.Supports("adyen-online").Should().BeTrue();
        policy.Supports("STRIPE").Should().BeFalse();
    }

    [Theory]
    [InlineData("http://api.example.com/v72")]
    [InlineData("https://user:pass@api.example.com/v72")]
    [InlineData("https://127.0.0.1/v72")]
    [InlineData("https://10.0.0.5/v72")]
    [InlineData("https://192.168.1.10/v72")]
    [InlineData("https://169.254.169.254/v72")]
    [InlineData("https://172.16.0.1/v72")]
    [InlineData("not-a-url")]
    [InlineData(null)]
    public void Shared_transport_guard_rejects_unsafe_urls(string? url) =>
        SafeHttpsUrl.TryParse(url, out _).Should().BeFalse();

    [Fact]
    public void Shared_transport_guard_accepts_public_https_urls() =>
        SafeHttpsUrl.TryParse("https://api.example.com/v72", out _).Should().BeTrue();

    private static MakePaymentRequestValidator CreateValidator() =>
        new(new PaymentProviderCatalog());

    private static MakePaymentRequest ValidRequest() => new()
    {
        ProviderName = PaymentConstants.AdyenOnlineProvider,
        Amount = 25.50m,
        CurrencyCode = "USD",
        OrderId = "order-1"
    };

    private static PaymentProvider Provider() => new()
    {
        ProviderName = PaymentConstants.AdyenOnlineProvider,
        ReturnUrl = "https://payments.example.com/payments/validate-payment",
        FrontendResultUrl = "https://app.merchant.example/payment-result"
    };

    private static IOptionsMonitor<PaymentOptions> Monitor(PaymentOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(x => x.CurrentValue).Returns(options);
        return monitor.Object;
    }
}
