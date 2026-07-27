using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
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
        protector.Setup(p => p.TryProtect(
                It.IsAny<string>(), out It.Ref<ProtectedProviderToken>.IsAny))
            .Returns(false);
        var service = new StoredPaymentMethodLifecycleService(
            methods.Object,
            Mock.Of<IPaymentRepository>(),
            protector.Object,
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

        public StoredPaymentMethodLifecycleService Service
        {
            get;
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
                    new ProviderTokenProtector(
                        keyRing),
                    Mock.Of<
                        ILogger<
                            StoredPaymentMethodLifecycleService>>());
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
