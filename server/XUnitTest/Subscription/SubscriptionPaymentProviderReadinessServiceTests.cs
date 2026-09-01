using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// The five readiness outcomes, for both providers -- see
/// <see cref="SubscriptionPaymentProviderReadiness"/>'s own remarks for why this single
/// evaluation backs the merchant profile GET, its save validation, and subscription creation's
/// pre-persist check alike.
/// </summary>
public sealed class SubscriptionPaymentProviderReadinessServiceTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<IPaymentProviderCatalog> _catalog = new();
    private readonly Mock<IPaymentRepository> _providers = new();
    private readonly Mock<IPaymentProviderSecretHydrator> _secrets = new();

    public SubscriptionPaymentProviderReadinessServiceTests()
    {
        _catalog
            .Setup(catalog => catalog.IsRegistered(It.IsAny<string>()))
            .Returns(true);
    }

    [Fact]
    public async Task An_unregistered_provider_is_unsupported()
    {
        _catalog.Setup(catalog => catalog.IsRegistered("BOGUS")).Returns(false);

        var result = await Service().CheckAsync(TenantId, null, "BOGUS", CancellationToken.None);

        result.Should().Be(SubscriptionPaymentProviderReadiness.Unsupported);
    }

    [Fact]
    public async Task No_stored_configuration_is_not_configured()
    {
        _providers
            .Setup(providers => providers.GetProvidersAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Service().CheckAsync(
            TenantId, null, PaymentConstants.StripeProvider, CancellationToken.None);

        result.Should().Be(SubscriptionPaymentProviderReadiness.NotConfigured);
    }

    [Fact]
    public async Task A_disabled_configuration_is_reported_disabled_not_not_configured()
    {
        Configured(new PaymentProvider
        {
            ProviderName = PaymentConstants.StripeProvider,
            IsEnabled = false,
            ApiBaseUrl = "https://api.stripe.com",
            MerchantId = "merchant"
        });

        var result = await Service().CheckAsync(
            TenantId, null, PaymentConstants.StripeProvider, CancellationToken.None);

        result.Should().Be(SubscriptionPaymentProviderReadiness.Disabled);
    }

    [Fact]
    public async Task A_missing_base_url_or_merchant_id_is_misconfigured()
    {
        Configured(new PaymentProvider
        {
            ProviderName = PaymentConstants.StripeProvider,
            IsEnabled = true,
            ApiBaseUrl = "",
            MerchantId = "merchant"
        });

        var result = await Service().CheckAsync(
            TenantId, null, PaymentConstants.StripeProvider, CancellationToken.None);

        result.Should().Be(SubscriptionPaymentProviderReadiness.Misconfigured);
    }

    [Fact]
    public async Task Hydration_failure_reports_credentials_unavailable()
    {
        Configured(new PaymentProvider
        {
            ProviderName = PaymentConstants.StripeProvider,
            IsEnabled = true,
            ApiBaseUrl = "https://api.stripe.com",
            MerchantId = "merchant"
        });
        _secrets
            .Setup(secrets => secrets.HydrateAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().CheckAsync(
            TenantId, null, PaymentConstants.StripeProvider, CancellationToken.None);

        result.Should().Be(SubscriptionPaymentProviderReadiness.CredentialsUnavailable);
    }

    [Fact]
    public async Task Stripe_needs_only_the_webhook_secret()
    {
        var provider = Configured(new PaymentProvider
        {
            ProviderName = PaymentConstants.StripeProvider,
            IsEnabled = true,
            ApiBaseUrl = "https://api.stripe.com",
            MerchantId = "merchant"
        });
        HydratesTo(provider, standardWebhookHmacKey: "whsec_abc");

        var result = await Service().CheckAsync(
            TenantId, null, PaymentConstants.StripeProvider, CancellationToken.None);

        result.Should().Be(SubscriptionPaymentProviderReadiness.Ready);
    }

    [Fact]
    public async Task Adyen_needs_the_api_key_and_both_webhook_keys()
    {
        var provider = Configured(new PaymentProvider
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            IsEnabled = true,
            ApiBaseUrl = "https://checkout-test.adyen.com",
            MerchantId = "merchant"
        });
        // API key present, but only one of the two webhook keys -- must not read as Ready.
        HydratesTo(provider, apiKey: "AQE...", standardWebhookHmacKey: "abc123");

        var result = await Service().CheckAsync(
            TenantId, null, PaymentConstants.AdyenOnlineProvider, CancellationToken.None);

        result.Should().Be(SubscriptionPaymentProviderReadiness.CredentialsUnavailable);
    }

    [Fact]
    public async Task Adyen_is_ready_once_every_required_secret_is_present()
    {
        var provider = Configured(new PaymentProvider
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            IsEnabled = true,
            ApiBaseUrl = "https://checkout-test.adyen.com",
            MerchantId = "merchant"
        });
        HydratesTo(
            provider,
            apiKey: "AQE...",
            standardWebhookHmacKey: "abc123",
            tokenWebhookHmacKey: "def456");

        var result = await Service().CheckAsync(
            TenantId, null, PaymentConstants.AdyenOnlineProvider, CancellationToken.None);

        result.Should().Be(SubscriptionPaymentProviderReadiness.Ready);
    }

    private PaymentProvider Configured(PaymentProvider provider)
    {
        _providers
            .Setup(providers => providers.GetProvidersAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([provider]);

        _secrets
            .Setup(secrets => secrets.HydrateAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return provider;
    }

    private void HydratesTo(
        PaymentProvider provider,
        string? apiKey = null,
        string? standardWebhookHmacKey = null,
        string? tokenWebhookHmacKey = null)
    {
        _secrets
            .Setup(secrets => secrets.HydrateAsync(provider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                provider.ApiKey = apiKey ?? provider.ApiKey;
                provider.StandardWebhookHmacKey = standardWebhookHmacKey;
                provider.TokenWebhookHmacKey = tokenWebhookHmacKey;
                return true;
            });
    }

    private ISubscriptionPaymentProviderReadinessService Service() =>
        new SubscriptionPaymentProviderReadinessService(
            _catalog.Object,
            _providers.Object,
            _secrets.Object,
            Options.Create(new PaymentOptions()));
}
