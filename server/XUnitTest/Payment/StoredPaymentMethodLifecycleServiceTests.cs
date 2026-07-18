using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

            var options = new PaymentOptions
            {
                ActiveProviderTokenEncryptionKeyId =
                    "key-1",
                ProviderTokenEncryptionKeys =
                    new Dictionary<string, string>
                    {
                        ["key-1"] =
                            Convert.ToBase64String(
                                Enumerable.Range(1, 32)
                                    .Select(
                                        value =>
                                            (byte)value)
                                    .ToArray())
                    }
            };
            var monitor =
                new Mock<IOptionsMonitor<PaymentOptions>>();
            monitor.SetupGet(value => value.CurrentValue)
                .Returns(options);

            Service =
                new StoredPaymentMethodLifecycleService(
                    Methods.Object,
                    Payments.Object,
                    new ProviderTokenProtector(
                        monitor.Object),
                    Mock.Of<
                        ILogger<
                            StoredPaymentMethodLifecycleService>>());
        }

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
