using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StoredPaymentMethodLifecycleServiceTests
{
    [Fact]
    public async Task Token_created_is_stored_only_after_correlated_authorisation()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook(
            "recurring.token.created");
        fixture.Payments
            .Setup(repository =>
                repository.GetByPspReferenceAsync(
                    "tenant-1",
                    "psp-1",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PaymentDetail
                {
                    TenantId = "tenant-1",
                    PaymentStatus =
                        PaymentStatuses.Authorized,
                    PspReference = "psp-1",
                    ShopperReference =
                        "shopper-reference",
                    RememberCard = true
                });

        await fixture.Service.ApplyTokenEventAsync(
            webhook,
            CancellationToken.None);

        fixture.Methods.Verify(
            repository =>
                repository.UpsertFromProviderAsync(
                    It.Is<StoredPaymentMethod>(
                        method =>
                            method.StoredPaymentMethodToken ==
                            null &&
                            method.ProviderTokenCiphertext !=
                            null &&
                            method.ProviderTokenFingerprint !=
                            null &&
                            method.Status ==
                            PaymentMethodStatus.Active),
                    webhook.EventDateUtc,
                    It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Token_created_waits_when_authorisation_is_missing()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook(
            "recurring.token.created");

        var action = async () =>
            await fixture.Service.ApplyTokenEventAsync(
                webhook,
                CancellationToken.None);

        await action.Should()
            .ThrowAsync<InvalidOperationException>();

        fixture.Methods.Verify(
            repository =>
                repository.UpsertFromProviderAsync(
                    It.IsAny<StoredPaymentMethod>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Finding 3's two-signal state machine: a card-setup payment need not have reached
    /// Authorized before its token is confirmed -- that status is now withheld until the token
    /// signal arrives too, so requiring it here (the ordinary, non-setup rule) would deadlock a
    /// setup whose token arrives first.
    /// </summary>
    [Fact]
    public async Task Token_created_for_a_setup_flow_payment_is_stored_before_authorisation_completes()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("recurring.token.created");
        var payment = new PaymentDetail
        {
            ItemId = "payment-1",
            TenantId = "tenant-1",
            PaymentFlow = PaymentFlows.PaymentMethodSetup,
            PaymentStatus = PaymentStatuses.Processing,
            PspReference = "psp-1",
            ShopperReference = "shopper-reference",
            RememberCard = true
        };
        fixture.Payments
            .Setup(repository => repository.GetByPspReferenceAsync(
                "tenant-1", "psp-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        fixture.Payments
            .Setup(repository => repository.GetByIdAsync(
                "tenant-1", "payment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        await fixture.Service.ApplyTokenEventAsync(webhook, CancellationToken.None);

        fixture.Methods.Verify(
            repository => repository.UpsertFromProviderAsync(
                It.IsAny<StoredPaymentMethod>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the token is durable proof of consent on its own and is worth recording immediately");
        fixture.Payments.Verify(
            repository => repository.TryRecordSetupTokenConfirmedAsync(
                "tenant-1", "payment-1", webhook.EventDateUtc, It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Payments.Verify(
            repository => repository.ApplyAuthorisationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<decimal>(),
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<PaymentInstrument?>(), It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "the authorisation signal has not arrived yet, so the setup must not be reported " +
            "Ready off the token alone");
    }

    [Fact]
    public async Task Token_created_completes_the_setup_when_authorisation_was_already_confirmed()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("recurring.token.created");
        var payment = new PaymentDetail
        {
            ItemId = "payment-1",
            TenantId = "tenant-1",
            PaymentFlow = PaymentFlows.PaymentMethodSetup,
            PaymentStatus = PaymentStatuses.Processing,
            PspReference = "psp-1",
            ShopperReference = "shopper-reference",
            RememberCard = true,
            // The Standard AUTHORISATION webhook already ran and recorded its own signal --
            // simulating the token arriving second.
            SetupAuthorizationConfirmedAtUtc = webhook.EventDateUtc.AddSeconds(-5)
        };
        fixture.Payments
            .Setup(repository => repository.GetByPspReferenceAsync(
                "tenant-1", "psp-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        fixture.Payments
            .Setup(repository => repository.GetByIdAsync(
                "tenant-1", "payment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        // Mirrors the real repository's write: the token signal must actually land on the same
        // in-memory record GetByIdAsync re-reads below, or the re-read can never see it.
        fixture.Payments
            .Setup(repository => repository.TryRecordSetupTokenConfirmedAsync(
                "tenant-1", "payment-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, DateTime eventDateUtc, CancellationToken _) =>
                payment.SetupTokenConfirmedAtUtc ??= eventDateUtc)
            .ReturnsAsync(true);
        fixture.Payments
            .Setup(repository => repository.ApplyAuthorisationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<decimal>(),
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<PaymentInstrument?>(), It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await fixture.Service.ApplyTokenEventAsync(webhook, CancellationToken.None);

        fixture.Payments.Verify(
            repository => repository.ApplyAuthorisationAsync(
                "tenant-1", "payment-1", true, 0m, false,
                It.IsAny<string>(), It.IsAny<DateTime>(), null,
                It.Is<PaymentOutboxEvent>(outboxEvent =>
                    outboxEvent.DeduplicationKey == "payment-1:PaymentMethodSetupSucceeded:setup-ready"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "both signals are now on the record, so the token arriving second must be the one " +
            "that completes the setup");
    }

    /// <summary>
    /// An explicit decline already recorded by the authorisation webhook is authoritative. A
    /// token arriving afterwards is not evidence the decline was wrong, and must not resurrect a
    /// setup that has already been told no.
    /// </summary>
    [Fact]
    public async Task Token_created_for_an_already_declined_setup_is_ignored()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("recurring.token.created");
        var payment = new PaymentDetail
        {
            ItemId = "payment-1",
            TenantId = "tenant-1",
            PaymentFlow = PaymentFlows.PaymentMethodSetup,
            PaymentStatus = PaymentStatuses.Refused,
            PspReference = "psp-1",
            ShopperReference = "shopper-reference",
            RememberCard = true
        };
        fixture.Payments
            .Setup(repository => repository.GetByPspReferenceAsync(
                "tenant-1", "psp-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        await fixture.Service.ApplyTokenEventAsync(webhook, CancellationToken.None);

        fixture.VerifyNoUpsert();
        fixture.Payments.Verify(
            repository => repository.TryRecordSetupTokenConfirmedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Token_disabled_never_reactivates_the_method()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook(
            "recurring.token.disabled");

        await fixture.Service.ApplyTokenEventAsync(
            webhook,
            CancellationToken.None);

        fixture.Methods.Verify(
            repository =>
                repository.MarkRemovedFromProviderAsync(
                    "tenant-1",
                    "shopper-reference",
                    It.Is<string>(
                        value =>
                            !value.Contains(
                                "provider-token")),
                    webhook.EventDateUtc,
                    It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Methods.Verify(
            repository =>
                repository.UpsertFromProviderAsync(
                    It.IsAny<StoredPaymentMethod>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Authorisation_without_save_consent_does_not_create_token()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook(
            "AUTHORISATION");

        await fixture.Service.ApplyAuthorisationTokenAsync(
            webhook,
            new PaymentDetail
            {
                ItemId = "payment-1",
                TenantId = "tenant-1",
                ShopperReference =
                    "shopper-reference",
                RememberCard = false
            },
            CancellationToken.None);

        fixture.Methods.Verify(
            repository =>
                repository.UpsertFromProviderAsync(
                    It.IsAny<StoredPaymentMethod>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Authorisation_without_token_is_ignored()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("AUTHORISATION");
        webhook.NormalizedPayload.StoredPaymentMethodToken = null;

        await fixture.Service.ApplyAuthorisationTokenAsync(
            webhook, PaymentWith(rememberCard: true), CancellationToken.None);

        fixture.VerifyNoUpsert();
    }

    [Fact]
    public async Task Authorisation_with_mismatched_shopper_is_rejected()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("AUTHORISATION");

        var act = () => fixture.Service.ApplyAuthorisationTokenAsync(
            webhook,
            PaymentWith(rememberCard: true, shopperReference: "someone-else"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Authorisation_with_consent_stores_the_token()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("AUTHORISATION");

        await fixture.Service.ApplyAuthorisationTokenAsync(
            webhook, PaymentWith(rememberCard: true), CancellationToken.None);

        fixture.Methods.Verify(repository =>
            repository.UpsertFromProviderAsync(
                It.Is<StoredPaymentMethod>(method =>
                    method.Status == PaymentMethodStatus.Active),
                webhook.EventDateUtc,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Stripe mints a fresh payment method on every checkout, so saving a card the shopper
    /// already holds arrives under a different token and was stored as a second card. The card
    /// fingerprint is stable across those tokens and identifies it as the one already held.
    /// </summary>
    [Fact]
    public async Task The_same_card_saved_again_moves_the_existing_record_onto_the_new_token()
    {
        var fixture = new Fixture();
        fixture.ArrangeSameCardAlreadySaved("card-fingerprint", "existing-1");
        var webhook = fixture.TokenWebhook("AUTHORISATION");

        await fixture.Service.ApplyAuthorisationTokenAsync(
            webhook, PaymentWith(rememberCard: true), CancellationToken.None);

        fixture.Methods.Verify(repository => repository.SupersedeTokenAsync(
                It.Is<StoredPaymentMethod>(method => method.ItemId == "existing-1"),
                webhook.EventDateUtc,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // No second row for a card the shopper already has.
        fixture.VerifyNoUpsert();
    }

    /// <summary>
    /// A token is only usable at the merchant account that issued it, and organizations within
    /// a tenant may be separate businesses with their own accounts. So the card is recorded
    /// against the organization that paid, taken from the payment.
    /// </summary>
    [Fact]
    public async Task A_stored_card_records_the_organization_whose_account_holds_it()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("AUTHORISATION");
        var payment = PaymentWith(rememberCard: true);
        payment.OrganizationId = "organization-1";

        await fixture.Service.ApplyAuthorisationTokenAsync(
            webhook, payment, CancellationToken.None);

        fixture.Methods.Verify(repository => repository.UpsertFromProviderAsync(
                It.Is<StoredPaymentMethod>(method =>
                    method.OrganizationId == "organization-1"),
                webhook.EventDateUtc,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Organizations that are subscribers of one tenant-level account, not merchants in their
    /// own right, must have their cards encrypted under the tenant's ring — never a ring named
    /// for the organization, which does not exist and never will.
    /// </summary>
    [Fact]
    public async Task A_card_saved_under_a_tenant_level_provider_is_encrypted_at_tenant_scope()
    {
        var fixture = new Fixture();
        fixture.Providers
            .Setup(cache => cache.GetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(new PaymentProvider
            {
                TenantId = "tenant-1",
                OrganizationId = null
            });

        var payment = PaymentWith(rememberCard: true);
        payment.OrganizationId = "caller-org";

        await fixture.Service.ApplyAuthorisationTokenAsync(
            fixture.TokenWebhook("AUTHORISATION"), payment, CancellationToken.None);

        fixture.Methods.Verify(repository => repository.UpsertFromProviderAsync(
                It.Is<StoredPaymentMethod>(method =>
                    method.OrganizationId == "caller-org" &&
                    method.EncryptionOrganizationId == null &&
                    method.EncryptionScopeResolvedAtUtc != null),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "visibility stays with the caller; encryption follows the merchant account, which " +
            "here is the tenant-level one");
    }

    /// <summary>
    /// The other model this module supports: an organization with its own merchant account.
    /// There the two fields agree, which is what made the bug easy to miss.
    /// </summary>
    [Fact]
    public async Task A_card_saved_under_an_organization_scoped_provider_is_encrypted_at_that_scope()
    {
        var fixture = new Fixture();
        fixture.Providers
            .Setup(cache => cache.GetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(new PaymentProvider
            {
                TenantId = "tenant-1",
                OrganizationId = "merchant-org"
            });

        var payment = PaymentWith(rememberCard: true);
        payment.OrganizationId = "merchant-org";

        await fixture.Service.ApplyAuthorisationTokenAsync(
            fixture.TokenWebhook("AUTHORISATION"), payment, CancellationToken.None);

        fixture.Methods.Verify(repository => repository.UpsertFromProviderAsync(
                It.Is<StoredPaymentMethod>(method =>
                    method.EncryptionOrganizationId == "merchant-org"),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task No_provider_configuration_fails_closed_rather_than_guessing_a_scope()
    {
        var fixture = new Fixture();
        fixture.Providers
            .Setup(cache => cache.GetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync((PaymentProvider?)null);

        var act = () => fixture.Service.ApplyAuthorisationTokenAsync(
            fixture.TokenWebhook("AUTHORISATION"),
            PaymentWith(rememberCard: true),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.VerifyNoUpsert();
    }

    [Fact]
    public async Task Authorisation_for_inactive_method_without_fresh_consent_is_skipped()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("AUTHORISATION");
        fixture.Methods
            .Setup(repository => repository.GetByTokenFingerprintAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredPaymentMethod
            {
                ItemId = "existing-1",
                Status = PaymentMethodStatus.Removed
            });
        fixture.Methods
            .Setup(repository => repository.ReactivateAfterFreshConsentAsync(
                It.IsAny<StoredPaymentMethod>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await fixture.Service.ApplyAuthorisationTokenAsync(
            webhook, PaymentWith(rememberCard: true), CancellationToken.None);

        fixture.VerifyNoUpsert();
    }

    [Fact]
    public async Task Token_event_without_token_is_rejected()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("recurring.token.created");
        webhook.NormalizedPayload.StoredPaymentMethodToken = null;

        var act = () => fixture.Service.ApplyTokenEventAsync(
            webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Token_event_for_inactive_local_method_is_skipped()
    {
        var fixture = new Fixture();
        var webhook = fixture.TokenWebhook("recurring.token.created");
        fixture.Methods
            .Setup(repository => repository.GetByTokenFingerprintAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredPaymentMethod
            {
                ItemId = "existing-1",
                Status = PaymentMethodStatus.Removed
            });

        await fixture.Service.ApplyTokenEventAsync(webhook, CancellationToken.None);

        fixture.VerifyNoUpsert();
    }

    [Fact]
    public async Task Token_event_without_configured_protection_is_rejected()
    {
        var methods = new Mock<IStoredPaymentMethodRepository>();
        methods.Setup(repository => repository.GetByTokenFingerprintAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredPaymentMethod
            {
                Status = PaymentMethodStatus.Active
            });
        var protector = new Mock<IProviderTokenProtector>();
        protector.Setup(p => p.CreateFingerprint(It.IsAny<string>()))
            .Returns("fingerprint");
        protector.Setup(p => p.ProtectAsync(
                It.IsAny<PaymentEncryptionScope>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProviderTokenProtectionResult.Failed);
        var providers = new Mock<IPaymentProviderCache>();
        providers.Setup(cache => cache.GetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(new PaymentProvider { TenantId = "tenant-1" });
        var service = new StoredPaymentMethodLifecycleService(
            methods.Object,
            Mock.Of<IPaymentRepository>(),
            protector.Object,
            Mock.Of<IStoredPaymentMethodDetailProviderGatewayResolver>(),
            Mock.Of<IStoredPaymentMethodProviderGatewayResolver>(),
            providers.Object,
            new PaymentOutboxEventFactory(),
            Mock.Of<ILogger<StoredPaymentMethodLifecycleService>>());
        var webhook = new PaymentWebhookInbox
        {
            TenantId = "tenant-1",
            EventCode = "recurring.token.created",
            EventDateUtc = DateTime.UtcNow,
            NormalizedPayload = new PaymentWebhookPayload
            {
                ShopperReference = "shopper-reference",
                StoredPaymentMethodToken = "provider-token",
                ProviderName = "ADYEN-ONLINE"
            }
        };

        var act = () => service.ApplyTokenEventAsync(webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static PaymentDetail PaymentWith(
        bool rememberCard,
        string shopperReference = "shopper-reference") =>
        new()
        {
            ItemId = "payment-1",
            TenantId = "tenant-1",
            ShopperReference = shopperReference,
            RememberCard = rememberCard
        };

    private sealed class Fixture
    {
        public Mock<IStoredPaymentMethodRepository> Methods
        {
            get;
        } = new();

        public Mock<IPaymentRepository> Payments
        {
            get;
        } = new();

        /// <summary>
        /// The provider a token is encrypted under. Defaults to a tenant-level configuration,
        /// which is the ordinary case; a test caring about organization-scoped encryption
        /// arranges its own <see cref="PaymentProvider.OrganizationId"/> here.
        /// </summary>
        public Mock<IPaymentProviderCache> Providers
        {
            get;
        } = new();

        public StoredPaymentMethodLifecycleService Service
        {
            get;
            private set;
        }

        public Fixture()
        {
            Methods
                .Setup(repository =>
                    repository.GetByTokenFingerprintAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    (StoredPaymentMethod?)null);

            Providers
                .Setup(cache => cache.GetAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Func<Task<PaymentProvider?>>>()))
                .ReturnsAsync(new PaymentProvider
                {
                    TenantId = "tenant-1",
                    ProviderName = PaymentConstants.AdyenOnlineProvider
                });

            var keyRing =
                new ProviderTokenEncryptionKeyRing(
                    "key-1",
                    new Dictionary<string, byte[]>
                    {
                        ["key-1"] =
                            Enumerable.Range(1, 32)
                                .Select(
                                    value =>
                                        (byte)value)
                                .ToArray()
                    });

            Service =
                new StoredPaymentMethodLifecycleService(
                    Methods.Object,
                    Payments.Object,
                    new ProviderTokenProtector(new AesGcmSecretProtector(new FixedKeyRingProvider(keyRing))),
                    Mock.Of<IStoredPaymentMethodDetailProviderGatewayResolver>(),
                    Mock.Of<IStoredPaymentMethodProviderGatewayResolver>(),
                    Providers.Object,
                    new PaymentOutboxEventFactory(),
                    Mock.Of<
                        ILogger<
                            StoredPaymentMethodLifecycleService>>());
        }

        /// <summary>
        /// Makes the provider describe the card, as one that mints a new token per checkout
        /// does, and report an existing record already holding that same card.
        /// </summary>
        public void ArrangeSameCardAlreadySaved(string cardFingerprint, string existingItemId)
        {
            var details = new Mock<IStoredPaymentMethodDetailProviderGateway>();
            details.Setup(gateway => gateway.Supports(It.IsAny<string>())).Returns(true);
            details.Setup(gateway => gateway.GetAsync(
                    It.IsAny<PaymentProvider>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StoredPaymentMethodDetail(
                    "card", "visa", "4242", "12", "2030", "credit", "US", cardFingerprint));

            var resolver = new Mock<IStoredPaymentMethodDetailProviderGatewayResolver>();
            resolver.Setup(item => item.Resolve(It.IsAny<string>())).Returns(details.Object);

            var providers = new Mock<IPaymentProviderCache>();
            providers.Setup(cache => cache.GetAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Func<Task<PaymentProvider?>>>()))
                .ReturnsAsync(new PaymentProvider { TenantId = "tenant-1", ProviderName = "provider" });

            Methods.Setup(repository => repository.GetByCardFingerprintAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), cardFingerprint, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StoredPaymentMethod
                {
                    ItemId = existingItemId,
                    TenantId = "tenant-1",
                    ProviderName = "provider",
                    ProviderTokenFingerprint = "a-different-token",
                    ProviderCardFingerprint = cardFingerprint,
                    Status = PaymentMethodStatus.Active
                });
            Methods.Setup(repository => repository.SupersedeTokenAsync(
                    It.IsAny<StoredPaymentMethod>(), It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var keyRing = new ProviderTokenEncryptionKeyRing(
                "key-1",
                new Dictionary<string, byte[]>
                {
                    ["key-1"] = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()
                });

            Service = new StoredPaymentMethodLifecycleService(
                Methods.Object,
                Payments.Object,
                new ProviderTokenProtector(new AesGcmSecretProtector(new FixedKeyRingProvider(keyRing))),
                resolver.Object,
                Mock.Of<IStoredPaymentMethodProviderGatewayResolver>(),
                providers.Object,
                new PaymentOutboxEventFactory(),
                Mock.Of<ILogger<StoredPaymentMethodLifecycleService>>());
        }

        public void VerifyNoUpsert() =>
            Methods.Verify(repository => repository.UpsertFromProviderAsync(
                    It.IsAny<StoredPaymentMethod>(), It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

        public PaymentWebhookInbox TokenWebhook(
            string eventCode) =>
            new()
            {
                TenantId = "tenant-1",
                EventCode = eventCode,
                EventDateUtc =
                    new DateTime(
                        2026,
                        7,
                        18,
                        10,
                        0,
                        0,
                        DateTimeKind.Utc),
                NormalizedPayload =
                    new PaymentWebhookPayload
                    {
                        EventId = "psp-1",
                        ProviderName =
                            PaymentConstants
                                .AdyenOnlineProvider,
                        ShopperReference =
                            "shopper-reference",
                        StoredPaymentMethodToken =
                            "provider-token",
                        PaymentMethodType = "scheme",
                        Brand = "visa",
                        LastFour = "1111"
                    }
            };
    }
}
