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

        result.Readiness.Should().Be(SubscriptionPaymentProviderReadiness.Unsupported);
    }

    [Fact]
    public async Task No_stored_configuration_is_not_configured()
    {
        _providers
            .Setup(providers => providers.GetProvidersAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Service().CheckAsync(
            TenantId, null, PaymentConstants.StripeProvider, CancellationToken.None);

        result.Readiness.Should().Be(SubscriptionPaymentProviderReadiness.NotConfigured);
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

        result.Readiness.Should().Be(SubscriptionPaymentProviderReadiness.Disabled);
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

        result.Readiness.Should().Be(SubscriptionPaymentProviderReadiness.Misconfigured);
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

        result.Readiness.Should().Be(SubscriptionPaymentProviderReadiness.CredentialsUnavailable);
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

        result.Readiness.Should().Be(SubscriptionPaymentProviderReadiness.Ready);
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

        result.Readiness.Should().Be(SubscriptionPaymentProviderReadiness.CredentialsUnavailable);
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

        result.Readiness.Should().Be(SubscriptionPaymentProviderReadiness.Ready);
    }

    /// <summary>
    /// Proves finding 7 is actually fixed, against the real scope-fallback semantics rather than
    /// a mock told what to answer: an organization-specific configuration that is disabled must
    /// not make the whole chain read Disabled when a broader (tenant-wide, here) configuration is
    /// enabled -- because that is exactly what a real charge would resolve through. Uses a hand
    /// -rolled <see cref="IPaymentRepository"/> that reimplements the same enabled-filtered,
    /// scope-chain-fallback semantics <see cref="PaymentRepository.GetProviderAsync"/> documents,
    /// so this test would fail if the readiness service ever went back to picking only the first
    /// scope match instead of deferring to the repository's own resolution.
    /// </summary>
    [Fact]
    public async Task An_organization_specific_disabled_configuration_still_resolves_ready_through_a_tenant_wide_fallback()
    {
        var organizationSpecificDisabled = new PaymentProvider
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            OrganizationId = "org-a",
            IsEnabled = false,
            ApiBaseUrl = "https://checkout-test.adyen.com",
            MerchantId = "org-a-merchant"
        };
        var tenantWideEnabled = new PaymentProvider
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            OrganizationId = null,
            IsEnabled = true,
            ApiBaseUrl = "https://checkout-test.adyen.com",
            MerchantId = "tenant-merchant",
            ApiKey = "AQE...",
            StandardWebhookHmacKey = "abc123",
            TokenWebhookHmacKey = "def456"
        };
        var repository = new ScopeFallbackFakePaymentRepository(
            [organizationSpecificDisabled, tenantWideEnabled]);
        var secrets = new Mock<IPaymentProviderSecretHydrator>();
        secrets
            .Setup(hydrator => hydrator.HydrateAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new SubscriptionPaymentProviderReadinessService(
            _catalog.Object, repository, secrets.Object, Options.Create(new PaymentOptions()));

        var result = await service.CheckAsync(
            TenantId, "org-a", PaymentConstants.AdyenOnlineProvider, CancellationToken.None);

        result.Readiness.Should().Be(SubscriptionPaymentProviderReadiness.Ready,
            "the real payment module would resolve this exact charge through the tenant-wide " +
            "configuration once org-a's own is filtered out for being disabled -- readiness must " +
            "agree, not reject what a real charge would actually succeed at");
        result.Provider.Should().BeSameAs(tenantWideEnabled);
    }

    /// <summary>
    /// Mimics <c>PaymentRepository.GetProviderAsync</c>'s own documented behaviour -- enabled
    /// -filtered, most-specific-scope-first with fallback -- entirely in memory, so this test
    /// exercises the real algorithm rather than a mock that simply echoes back whatever answer
    /// the test wants.
    /// </summary>
    private sealed class ScopeFallbackFakePaymentRepository : IPaymentRepository
    {
        private readonly List<PaymentProvider> _all;

        public ScopeFallbackFakePaymentRepository(List<PaymentProvider> all) => _all = all;

        public Task<PaymentProvider?> GetProviderAsync(
            string tenantId, string? organizationId, string providerName,
            CancellationToken cancellationToken)
        {
            foreach (var candidate in global::Payment.DomainService.Utilities.PaymentProviderScopeChain.Candidates(
                         organizationId, new PaymentOptions()))
            {
                var found = _all.FirstOrDefault(provider =>
                    string.Equals(provider.ProviderName, providerName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(provider.OrganizationId, candidate, StringComparison.Ordinal) &&
                    provider.IsEnabled);

                if (found is not null)
                {
                    return Task.FromResult<PaymentProvider?>(found);
                }
            }

            return Task.FromResult<PaymentProvider?>(null);
        }

        public Task<IReadOnlyList<PaymentProvider>> GetProvidersAsync(
            string tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PaymentProvider>>(_all);

        public Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> TryCreateAsync(
            global::Payment.DomainService.Entities.PaymentDetail payment,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<global::Payment.DomainService.Entities.PaymentDetail?> GetByIdAsync(
            string tenantId, string paymentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<global::Payment.DomainService.Entities.PaymentDetail?> GetByPspReferenceAsync(
            string tenantId, string pspReference, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<global::Payment.DomainService.Entities.PaymentDetail?> GetByIdempotencyKeyAsync(
            string tenantId, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<global::Payment.DomainService.Entities.PaymentDetail?> GetRecurringPaymentByOrderIdAsync(
            string tenantId, string orderId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<global::Payment.DomainService.Entities.PaymentDetail?> TryClaimInitiationAsync(
            string tenantId, string paymentId, string leaseId, DateTime leaseUntilUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SaveInitiationRequestAsync(
            string tenantId, string paymentId, string leaseId,
            global::Payment.DomainService.Models.ProviderInitiationRequest request,
            string frontendResultUrlSnapshot, string returnStateNonceHash, string shopperReference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CompleteInitiationAsync(
            string tenantId, string paymentId, string leaseId, string status, string? sessionId,
            string? sessionData, string? redirectUrl, DateTime? expiresAtUtc, string? failureCode,
            global::Payment.DomainService.Entities.PaymentOutboxEvent outboxEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkInitiationUnknownAsync(
            string tenantId, string paymentId, string leaseId, string failureCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CompleteStoredPaymentChargeInitiationAsync(
            string tenantId, string paymentId, string leaseId, string pspReference,
            string? providerResultCode, global::Payment.DomainService.Entities.PaymentOutboxEvent outboxEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SaveProviderRoutingAsync(
            string tenantId, string paymentId, string leaseId, string providerReference,
            string merchantAccount, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SaveCheckoutObservationAsync(
            string tenantId, string paymentId, string sessionStatus, string? resultCode,
            string sessionResultHash, string? pspReference,
            global::Payment.DomainService.Entities.PaymentInstrument? instrument,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ApplyAuthorisationAsync(
            string tenantId, string paymentId, bool authorized, decimal authorizedAmount,
            bool capturedAutomatically, string pspReference, DateTime eventDateUtc,
            global::Payment.DomainService.Entities.PaymentInstrument? instrument,
            global::Payment.DomainService.Entities.PaymentOutboxEvent outboxEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<List<global::Payment.DomainService.Entities.PaymentDetail>> GetPaymentsWithDueOutboxEventsAsync(
            string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryClaimOutboxEventAsync(
            string tenantId, string paymentId, string eventId, string leaseId,
            DateTime leaseUntilUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkOutboxPublishedAsync(
            string tenantId, string paymentId, string eventId, string leaseId, DateTime utcNow,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkOutboxFailedAsync(
            string tenantId, string paymentId, string eventId, string leaseId,
            global::Payment.DomainService.Enums.PaymentOutboxStatus status, int attemptCount,
            DateTime nextAttemptAtUtc, string error, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<List<global::Payment.DomainService.Entities.PaymentDetail>> GetStaleInitiationsAsync(
            string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasUnresolvedRecurringPaymentAsync(
            string tenantId, string storedPaymentMethodId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryCreateProviderAsync(
            PaymentProvider provider, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PaymentProvider?> GetProviderByIdAsync(
            string tenantId, string providerItemId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PaymentProvider?> TryUpdateProviderConfigurationAsync(
            string tenantId, string providerItemId, long expectedVersion, string frontendResultUrl,
            string? countryCode, bool manualCapture, int maxRefundDays, string? storeId,
            bool isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PaymentProvider?> TryRotateProviderCredentialsAsync(
            string tenantId, string providerItemId, long expectedVersion,
            string providerSecretsCiphertext, string tenantSecuritySecretsCiphertext,
            string encryptionKeyId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SaveProviderSecretsAsync(
            string tenantId, string providerItemId, string providerSecretsCiphertext,
            string tenantSecuritySecretsCiphertext, string encryptionKeyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ReplaceProviderSecretsAsync(
            string tenantId, string providerItemId, string expectedKeyId,
            string providerSecretsCiphertext, string tenantSecuritySecretsCiphertext,
            string encryptionKeyId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private PaymentProvider Configured(PaymentProvider provider, string? organizationId = null)
    {
        // GetProvidersAsync backs only the Disabled/NotConfigured diagnosis (see the type's own
        // remarks); the actual Ready decision goes through GetProviderAsync below, exactly the
        // lookup a real charge resolves its provider through -- enabled-filtered, the same way
        // the production repository filters it.
        _providers
            .Setup(providers => providers.GetProvidersAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([provider]);

        _providers
            .Setup(providers => providers.GetProviderAsync(
                TenantId, organizationId, provider.ProviderName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider.IsEnabled ? provider : null);

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
