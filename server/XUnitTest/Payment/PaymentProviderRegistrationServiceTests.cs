using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Payment.DomainService.Validators;

namespace XUnitTest.Payment;

public sealed class PaymentProviderRegistrationServiceTests
{
    private const string TenantId = "tenant-1";
    private const string KeyId = "key-1";
    private static readonly string ExistingShopperKey =
        Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray());

    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IPaymentExecutionContextResolver> _contextResolver = new();
    private readonly AesGcmSecretProtector _protector = new(
        new FixedKeyRingProvider(
            new ProviderTokenEncryptionKeyRing(
            KeyId,
            new Dictionary<string, byte[]>
            {
                [KeyId] = Enumerable.Repeat((byte)7, 32).ToArray()
            })));

    private PaymentProvider? _created;

    public PaymentProviderRegistrationServiceTests()
    {
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", null),
                null));
        _repository.Setup(x => x.TryCreateProviderAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentProvider, CancellationToken>((provider, _) => _created = provider)
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Organizations within a tenant may be separate businesses with their own merchant
    /// accounts, so a configuration belongs to one. Taken from the caller's context like the
    /// tenant, never the request body, so nobody can register against another organization.
    /// </summary>
    [Fact]
    public async Task A_provider_is_registered_against_the_callers_organization()
    {
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", "organization-1"),
                null));

        var result = await Service().RegisterAsync(Request(), "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().Be("organization-1");
    }

    /// <summary>
    /// A caller with no organization registers a tenant-level configuration, which is what
    /// every configuration predating organization scoping is.
    /// </summary>
    [Fact]
    public async Task A_caller_without_an_organization_registers_a_tenant_level_configuration()
    {
        var result = await Service().RegisterAsync(Request(), "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().BeNull();
    }

    [Fact]
    public async Task A_stripe_provider_is_created_with_derived_configuration()
    {
        var result = await Service().RegisterAsync(Request(), "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.TenantId.Should().Be(TenantId);
        _created.ProviderName.Should().Be(PaymentConstants.StripeProvider);
        _created.ApiBaseUrl.Should().Be("https://api.stripe.com");
        _created.ReturnUrl.Should().Be("https://payments.example/payments/validate");
        _created.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Credentials_are_encrypted_and_never_stored_in_the_clear()
    {
        await Service().RegisterAsync(Request(), "corr", CancellationToken.None);

        _created!.ProviderSecretsCiphertext.Should().NotBeNullOrWhiteSpace();
        _created.ProviderSecretsCiphertext.Should().NotContain("sk_test_123");
        _created.SecretsEncryptionKeyId.Should().Be(KeyId);
        _created.ApiKey.Should().BeEmpty("the plaintext field is hydrated at use, not stored");
    }

    [Fact]
    public async Task The_response_never_carries_a_credential_back_out()
    {
        var result = await Service().RegisterAsync(Request(), "corr", CancellationToken.None);

        JsonSerializer.Serialize(result.Payment)
            .Should().NotContain("sk_test_123").And.NotContain("whsec_");
    }

    [Fact]
    public async Task The_tenant_comes_from_context_not_from_the_request()
    {
        await Service().RegisterAsync(Request(), "corr", CancellationToken.None);

        _created!.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task Security_keys_are_generated_when_not_supplied()
    {
        await Service().RegisterAsync(Request(), "corr", CancellationToken.None);

        var security = await DecryptSecurityAsync();
        Convert.FromBase64String(security.ShopperReferenceHmacKey).Length.Should().Be(32);
        Convert.FromBase64String(security.ReturnStateHmac.Active).Length.Should().Be(32);
    }

    [Fact]
    public async Task A_supplied_shopper_reference_key_is_preserved_for_migration()
    {
        var request = Request();
        request.ShopperReferenceHmacKey = ExistingShopperKey;

        await Service().RegisterAsync(request, "corr", CancellationToken.None);

        // Regenerating this would change every derived shopper reference and orphan
        // previously stored payment methods.
        (await DecryptSecurityAsync()).ShopperReferenceHmacKey.Should().Be(ExistingShopperKey);
    }

    [Fact]
    public async Task A_duplicate_provider_and_merchant_is_a_conflict()
    {
        _repository.Setup(x => x.TryCreateProviderAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().RegisterAsync(Request(), "corr", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("payment_provider_already_registered");
    }

    [Fact]
    public async Task An_unregistered_provider_is_rejected_before_anything_is_written()
    {
        var request = Request();
        request.ProviderName = "PAYPAL";

        var result = await Service().RegisterAsync(request, "corr", CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_not_supported");
        _repository.Verify(x => x.TryCreateProviderAsync(
            It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_non_https_frontend_url_is_rejected()
    {
        var request = Request();
        request.FrontendResultUrl = "http://app.example/result";

        (await Service().RegisterAsync(request, "corr", CancellationToken.None))
            .ErrorCode.Should().Be("payment_frontend_url_invalid");
    }

    [Fact]
    public async Task A_malformed_Adyen_HMAC_is_rejected_before_storage()
    {
        var request = Request();
        request.ProviderName =
            PaymentConstants.AdyenOnlineProvider;
        request.ApiBaseUrl =
            "https://checkout-test.adyen.com/v72";
        request.WebhookHmacKey = "not-hex";
        request.TokenHmacKey = "not-hex";

        var result = await Service()
            .RegisterAsync(
                request,
                "corr",
                CancellationToken.None);

        result.ErrorCode.Should().Be(
            "payment_provider_credentials_invalid");
        _repository.Verify(repository =>
                repository.TryCreateProviderAsync(
                    It.IsAny<PaymentProvider>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Registration_is_unavailable_when_no_public_base_url_is_configured()
    {
        var result = await Service(publicBaseUrl: string.Empty)
            .RegisterAsync(Request(), "corr", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        _repository.Verify(x => x.TryCreateProviderAsync(
            It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private async Task<TenantPaymentSecuritySecret> DecryptSecurityAsync()
    {
        var read = await _protector.UnprotectAsync(
            PaymentEncryptionScope.From(_created!),
            _created.TenantSecuritySecretsCiphertext!,
            _created.SecretsEncryptionKeyId!);

        read.IsRead.Should().BeTrue();

        return JsonSerializer.Deserialize<TenantPaymentSecuritySecret>(
            read.Plaintext,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static RegisterPaymentProviderRequest Request() => new()
    {
        ProviderName = PaymentConstants.StripeProvider,
        MerchantId = "acct_123",
        FrontendResultUrl = "https://app.example/result",
        ApiKey = "sk_test_123",
        WebhookHmacKey = "whsec_abc",
        MaxRefundDays = 90
    };

    private PaymentProviderRegistrationService Service(
        string publicBaseUrl = "https://payments.example")
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(x => x.CurrentValue)
            .Returns(new PaymentOptions { PublicBaseUrl = publicBaseUrl });

        var catalog = new PaymentProviderCatalog();
        var endpointPolicies = new ProviderEndpointPolicyResolver(
        [
            new AdyenEndpointPolicy(),
            new StripeEndpointPolicy()
        ]);

        return new PaymentProviderRegistrationService(
            _contextResolver.Object,
            new RegisterPaymentProviderRequestValidator(
                catalog,
                endpointPolicies,
                new CheckoutUrlPolicy()),
            catalog,
            _protector,
            _repository.Object,
            options.Object,
            NullLogger<PaymentProviderRegistrationService>.Instance);
    }
}
