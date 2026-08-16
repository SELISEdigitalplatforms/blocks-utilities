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
    private readonly Mock<IOrganizationDirectory> _organizations = new();
    private readonly Mock<IPaymentKeyRingStore> _keyRingStore = new();
    private readonly Mock<IPaymentDistributedLock> _locks = new();
    private readonly Mock<IProviderTokenEncryptionKeyRingProvider> _keyRings = new();
    private readonly AesGcmSecretProtector _protector = new(
        new FixedKeyRingProvider(
            new ProviderTokenEncryptionKeyRing(
            KeyId,
            new Dictionary<string, byte[]>
            {
                [KeyId] = Enumerable.Repeat((byte)7, 32).ToArray()
            })));

    private PaymentProvider? _created;

    /// <summary>Every configuration written, so a multi-organization run can be inspected in full.</summary>
    private readonly List<PaymentProvider> _createdProviders = [];

    public PaymentProviderRegistrationServiceTests()
    {
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", null),
                null));
        _repository.Setup(x => x.TryCreateProviderAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentProvider, CancellationToken>((provider, _) =>
            {
                _created = provider;
                _createdProviders.Add(provider);
            })
            .ReturnsAsync(true);
        _organizations.Setup(x => x.FindAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.Found);

        // Default: the scope already has a ring of its own, so provisioning stays out of the
        // way of every test that is not about it.
        _keyRings.Setup(x => x.CheckAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentKeyRingHealth(
                true, "secret", false, KeyId, string.Empty));
        _keyRingStore.Setup(x => x.TryCreateAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyRingProvisionOutcome.Created);
        _locks.Setup(x => x.TryAcquireAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IPaymentLockHandle>());
    }

    /// <summary>
    /// Organizations within a tenant may be separate businesses with their own merchant
    /// accounts, so a configuration belongs to one. A request naming none takes the caller's
    /// context, which is what every registration did before the field existed.
    /// </summary>
    [Fact]
    public async Task A_provider_is_registered_against_the_callers_organization()
    {
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", "organization-1"),
                null));

        var result = await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

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
        var result = await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().BeNull();
    }

    [Fact]
    public async Task A_stripe_provider_is_created_with_derived_configuration()
    {
        var result = await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

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
        await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        _created!.ProviderSecretsCiphertext.Should().NotBeNullOrWhiteSpace();
        _created.ProviderSecretsCiphertext.Should().NotContain("sk_test_123");
        _created.SecretsEncryptionKeyId.Should().Be(KeyId);
        _created.ApiKey.Should().BeEmpty("the plaintext field is hydrated at use, not stored");
    }

    [Fact]
    public async Task The_response_never_carries_a_credential_back_out()
    {
        var result = await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        JsonSerializer.Serialize(result.Payment)
            .Should().NotContain("sk_test_123").And.NotContain("whsec_");
    }

    [Fact]
    public async Task The_tenant_comes_from_context_not_from_the_request()
    {
        await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        _created!.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task Security_keys_are_generated_when_not_supplied()
    {
        await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        var security = await DecryptSecurityAsync();
        Convert.FromBase64String(security.ShopperReferenceHmacKey).Length.Should().Be(32);
        Convert.FromBase64String(security.ReturnStateHmac.Active).Length.Should().Be(32);
    }

    [Fact]
    public async Task A_supplied_shopper_reference_key_is_preserved_for_migration()
    {
        var request = Request();
        request.ShopperReferenceHmacKey = ExistingShopperKey;

        await Service().RegisterOneAsync(request, "corr", CancellationToken.None);

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

        var result = await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("payment_provider_already_registered");
    }

    [Fact]
    public async Task An_unregistered_provider_is_rejected_before_anything_is_written()
    {
        var request = Request();
        request.ProviderName = "PAYPAL";

        var result = await Service().RegisterOneAsync(request, "corr", CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_not_supported");
        _repository.Verify(x => x.TryCreateProviderAsync(
            It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_non_https_frontend_url_is_rejected()
    {
        var request = Request();
        request.FrontendResultUrl = "http://app.example/result";

        (await Service().RegisterOneAsync(request, "corr", CancellationToken.None))
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
            .RegisterOneAsync(
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
            .RegisterOneAsync(Request(), "corr", CancellationToken.None);

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

    /// <summary>
    /// Puts the caller in the one organization whose requests may name another.
    /// </summary>
    private void SetupConsoleCaller() =>
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(
                    TenantId,
                    "actor-1",
                    TestPaymentOptions.ConsoleOrganizationId),
                null));

    /// <summary>
    /// The configuration console runs with a fixed default organization, so without this a
    /// tenant could only ever configure that one. The named organization wins over the
    /// context's.
    /// </summary>
    [Fact]
    public async Task A_named_organization_is_used_in_place_of_the_callers()
    {
        SetupConsoleCaller();

        var request = Request();
        request.OrganizationId = "organization-2";

        var result = await Service().RegisterOneAsync(request, "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().Be("organization-2");
    }

    /// <summary>
    /// A configuration decides which merchant account an organization's money moves through,
    /// and which key ring its credentials are encrypted against. An application carries its own
    /// organization, so its body can move neither.
    /// </summary>
    [Fact]
    public async Task An_application_cannot_configure_another_organization()
    {
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", "organization-1"),
                null));

        var request = Request();
        request.OrganizationId = "organization-2";

        var result = await Service().RegisterOneAsync(request, "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().Be("organization-1");
    }

    /// <summary>
    /// Naming no organization must not start calling IAM: that is the path every existing
    /// registration takes, and giving it a new dependency would give it a new way to fail.
    /// </summary>
    [Fact]
    public async Task An_unnamed_organization_never_reaches_the_directory()
    {
        await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        _organizations.Verify(
            x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Naming the organization the token already carries proves nothing the token did not,
    /// so it costs no round trip.
    /// </summary>
    [Fact]
    public async Task Naming_the_callers_own_organization_needs_no_verification()
    {
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", "organization-1"),
                null));

        var request = Request();
        request.OrganizationId = "organization-1";

        var result = await Service().RegisterOneAsync(request, "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _organizations.Verify(
            x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// An organization the directory does not know is the caller's mistake, and nothing is
    /// written under it. This is what stops the request body being a way to reach an
    /// organization the caller cannot see.
    /// </summary>
    [Fact]
    public async Task An_unknown_organization_is_refused_and_nothing_is_written()
    {
        SetupConsoleCaller();
        _organizations.Setup(x => x.FindAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.NotFound);

        var request = Request();
        request.OrganizationId = "no-such-organization";

        var result = await Service().RegisterOneAsync(request, "corr", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("organization_not_found");
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _created.Should().BeNull();
    }

    /// <summary>
    /// Unreachable is not the same answer as unknown. Proceeding would write configuration
    /// under an organization nobody confirmed, and encrypt it against that organization's key
    /// ring — so it fails closed and retryably instead.
    /// </summary>
    [Fact]
    public async Task An_unverifiable_organization_fails_closed_rather_than_guessing()
    {
        SetupConsoleCaller();
        _organizations.Setup(x => x.FindAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.Unavailable);

        var request = Request();
        request.OrganizationId = "organization-2";

        var result = await Service().RegisterOneAsync(request, "corr", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("organization_verification_unavailable");
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        _created.Should().BeNull();
    }

    /// <summary>
    /// The one that silently corrupts data if it regresses: the credentials must be encrypted
    /// under the key ring of the organization that was <em>selected</em>, not the caller's.
    /// Encrypt under the wrong ring and the provider is unreadable by the process that later
    /// resolves its own scope, which reads as "payments unavailable" with nothing to explain it.
    /// </summary>
    [Fact]
    public async Task Credentials_are_encrypted_under_the_selected_organizations_key_ring()
    {
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", "default"),
                null));

        // Only organization-2 has a ring. If registration were to encrypt under the caller's
        // "default" scope, there would be no key and the attempt would fail outright.
        var selectedScope = new PaymentEncryptionScope(TenantId, "organization-2");
        var protector = new AesGcmSecretProtector(
            new ScopedKeyRingProvider(new Dictionary<string, IProviderTokenEncryptionKeyRing>
            {
                [selectedScope.ToString()] = new ProviderTokenEncryptionKeyRing(
                    KeyId,
                    new Dictionary<string, byte[]>
                    {
                        [KeyId] = Enumerable.Repeat((byte)5, 32).ToArray()
                    })
            }));

        var request = Request();
        request.OrganizationId = "organization-2";

        var result = await Service(protector: protector)
            .RegisterOneAsync(request, "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().Be("organization-2");
        _created.SecretsEncryptionKeyId.Should().Be(KeyId);

        // And it round-trips under that scope, which is the real proof.
        var read = await protector.UnprotectAsync(
            PaymentEncryptionScope.From(_created),
            _created.ProviderSecretsCiphertext!,
            _created.SecretsEncryptionKeyId!);

        read.Plaintext.Should().Contain("sk_test_123");
    }

    /// <summary>
    /// The manual step this exists to remove: a scope with no ring used to fail registration
    /// with nothing pointing at the cause.
    /// </summary>
    [Fact]
    public async Task A_scope_without_a_key_ring_has_one_provisioned()
    {
        GivenNoKeyRingOfItsOwn();

        var result = await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _keyRingStore.Verify(
            x => x.TryCreateAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// With the shared fallback on, an unprovisioned scope reads perfectly well through the
    /// shared ring. Checking readability alone would therefore never provision anything, and
    /// every new organization would keep landing on the one key scoped rings exist to escape.
    /// </summary>
    [Fact]
    public async Task A_scope_running_on_the_shared_ring_is_still_provisioned()
    {
        _keyRings.Setup(x => x.CheckAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentKeyRingHealth(
                true, "secret", true, KeyId, string.Empty));

        await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        _keyRingStore.Verify(
            x => x.TryCreateAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_scope_with_its_own_ring_is_left_alone()
    {
        await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        _keyRingStore.Verify(
            x => x.TryCreateAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Provisioning_is_serialised_by_a_lock_on_the_secret_name()
    {
        GivenNoKeyRingOfItsOwn();

        await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        // Two first registrations for the same new organization would otherwise both find
        // nothing and both write, the second replacing the key the first had just used.
        _locks.Verify(
            x => x.TryAcquireAsync(
                It.Is<string>(resource => resource.Contains("payment-keyring", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_scope_that_cannot_be_provisioned_fails_closed()
    {
        GivenNoKeyRingOfItsOwn();
        _keyRingStore.Setup(x => x.TryCreateAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyRingProvisionOutcome.Unavailable);

        var result = await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_key_ring_unavailable");
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        _created.Should().BeNull();
    }

    [Fact]
    public async Task An_unavailable_lock_fails_closed_rather_than_writing_unguarded()
    {
        GivenNoKeyRingOfItsOwn();
        _locks.Setup(x => x.TryAcquireAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPaymentLockHandle?)null);

        var result = await Service().RegisterOneAsync(Request(), "corr", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_key_ring_unavailable");
        _keyRingStore.Verify(
            x => x.TryCreateAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Switching_auto_provisioning_off_restores_the_previous_behaviour()
    {
        GivenNoKeyRingOfItsOwn();

        await Service(autoProvisionKeyRing: false)
            .RegisterOneAsync(Request(), "corr", CancellationToken.None);

        _keyRings.Verify(
            x => x.CheckAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _keyRingStore.Verify(
            x => x.TryCreateAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void GivenNoKeyRingOfItsOwn() =>
        _keyRings.Setup(x => x.CheckAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentKeyRingHealth(
                false, "secret", false, string.Empty, "missing"));

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
        string publicBaseUrl = "https://payments.example",
        IAesGcmSecretProtector? protector = null,
        bool autoProvisionKeyRing = true)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(x => x.CurrentValue)
            .Returns(new PaymentOptions
            {
                PublicBaseUrl = publicBaseUrl,
                AutoProvisionKeyRing = autoProvisionKeyRing
            });

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
            protector ?? _protector,
            _repository.Object,
            // The real resolver over the mocked directory, so the assertions below about
            // when IAM is and is not called keep testing the actual policy.
            new PaymentOrganizationResolver(
                _organizations.Object,
                options.Object,
                NullLogger<PaymentOrganizationResolver>.Instance),
            _keyRings.Object,
            _keyRingStore.Object,
            _locks.Object,
            options.Object,
            NullLogger<PaymentProviderRegistrationService>.Instance);
    }

    /// <summary>
    /// A tenant whose organizations all bill through one merchant account would otherwise
    /// repeat the whole registration, credentials included, once per organization.
    /// </summary>
    [Fact]
    public async Task Several_organizations_each_get_their_own_configuration()
    {
        SetupConsoleCaller();
        var request = Request();
        request.OrganizationIds = ["organization-1", "organization-2", "organization-3"];

        var result = await Service().RegisterAsync(request, "corr", CancellationToken.None);

        result.AllSucceeded.Should().BeTrue();
        _createdProviders.Select(provider => provider.OrganizationId)
            .Should().Equal("organization-1", "organization-2", "organization-3");
    }

    /// <summary>
    /// Separate rows, not one shared row read by three organizations. That is what lets one be
    /// disabled or re-keyed without touching the others.
    /// </summary>
    [Fact]
    public async Task Each_organizations_configuration_is_a_row_of_its_own()
    {
        SetupConsoleCaller();
        var request = Request();
        request.OrganizationIds = ["organization-1", "organization-2"];

        await Service().RegisterAsync(request, "corr", CancellationToken.None);

        _createdProviders.Select(provider => provider.ItemId).Distinct()
            .Should().HaveCount(2);
    }

    /// <summary>
    /// Encrypted once per organization, under that organization's own key ring. Sharing one
    /// ciphertext across organizations would make scoped key rings cosmetic.
    /// </summary>
    [Fact]
    public async Task Each_organization_gets_its_own_key_ring_scope()
    {
        SetupConsoleCaller();
        var scopes = new List<PaymentEncryptionScope>();
        _keyRings.Setup(x => x.CheckAsync(
                It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentEncryptionScope, CancellationToken>((scope, _) => scopes.Add(scope))
            .ReturnsAsync(new PaymentKeyRingHealth(true, "secret", false, KeyId, string.Empty));

        var request = Request();
        request.OrganizationIds = ["organization-1", "organization-2"];

        await Service().RegisterAsync(request, "corr", CancellationToken.None);

        scopes.Select(scope => scope.OrganizationId)
            .Should().Equal("organization-1", "organization-2");
    }

    /// <summary>
    /// The organizations are independent, so one failing must not discard the configurations
    /// already written for the others — there is nothing to roll back to that would be more
    /// correct than what succeeded.
    /// </summary>
    [Fact]
    public async Task One_organization_failing_does_not_undo_the_others()
    {
        SetupConsoleCaller();
        _organizations.Setup(x => x.FindAsync(
                "organization-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.NotFound);

        var request = Request();
        request.OrganizationIds = ["organization-1", "organization-2", "organization-3"];

        var result = await Service().RegisterAsync(request, "corr", CancellationToken.None);

        result.AllSucceeded.Should().BeFalse();
        result.For("organization-1").IsSuccess.Should().BeTrue();
        result.For("organization-2").ErrorCode.Should().Be("organization_not_found");
        result.For("organization-3").IsSuccess.Should().BeTrue();
        _createdProviders.Select(provider => provider.OrganizationId)
            .Should().Equal("organization-1", "organization-3");
    }

    /// <summary>
    /// A partial result has to say which organization each verdict belongs to. Without it the
    /// caller knows something failed but not what to retry.
    /// </summary>
    [Fact]
    public async Task Every_named_organization_is_reported_back()
    {
        SetupConsoleCaller();
        var request = Request();
        request.OrganizationIds = ["organization-1", "organization-2"];

        var result = await Service().RegisterAsync(request, "corr", CancellationToken.None);

        result.Organizations.Select(outcome => outcome.OrganizationId)
            .Should().Equal("organization-1", "organization-2");
        result.Organizations.Should().OnlyContain(
            outcome => outcome.PaymentProviderId != null);
    }

    /// <summary>
    /// The singular field is shorthand for a one-element list, so a caller may use either
    /// without discovering that combining them registers the same organization twice.
    /// </summary>
    [Fact]
    public async Task The_singular_and_plural_fields_are_one_list()
    {
        SetupConsoleCaller();
        var request = Request();
        request.OrganizationId = "organization-1";
        request.OrganizationIds = ["organization-1", "organization-2"];

        var result = await Service().RegisterAsync(request, "corr", CancellationToken.None);

        result.AllSucceeded.Should().BeTrue();
        _createdProviders.Select(provider => provider.OrganizationId)
            .Should().Equal("organization-1", "organization-2");
    }

    /// <summary>
    /// Naming none is what every registration did before either field existed: one
    /// configuration, under whatever the caller resolves to.
    /// </summary>
    [Fact]
    public async Task Naming_no_organization_still_writes_exactly_one_configuration()
    {
        var result = await Service().RegisterAsync(
            Request(), "corr", CancellationToken.None);

        result.Organizations.Should().HaveCount(1);
        _createdProviders.Should().HaveCount(1);
    }

    /// <summary>
    /// The console rule is not bypassed by using the list: an application still cannot
    /// configure organizations it does not carry, however many it names.
    /// </summary>
    [Fact]
    public async Task An_application_naming_several_organizations_configures_only_its_own()
    {
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", "organization-1"),
                null));

        var request = Request();
        request.OrganizationIds = ["organization-2", "organization-3"];

        await Service().RegisterAsync(request, "corr", CancellationToken.None);

        _createdProviders.Should().OnlyContain(
            provider => provider.OrganizationId == "organization-1");
    }

    /// <summary>
    /// Every organization costs a directory lookup, a vault round trip and a write inside one
    /// request, so the list is bounded rather than whatever the caller sends.
    /// </summary>
    [Fact]
    public async Task An_oversized_list_is_refused_before_anything_is_written()
    {
        SetupConsoleCaller();
        var request = Request();
        request.OrganizationIds = Enumerable
            .Range(0, 51)
            .Select(index => $"organization-{index}")
            .ToArray();

        var result = await Service().RegisterAsync(request, "corr", CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("payment_provider_too_many_organizations");
        _createdProviders.Should().BeEmpty();
    }
}
