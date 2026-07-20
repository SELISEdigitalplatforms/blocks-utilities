using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentWebhookIntakeServiceTests
{
    private const string TenantId = "de9fc4f4baa4c4cbc829b6059b372dc6";
    private const string OtherTenantId = "f080a1bea04280a72149fd689d50a48c";
    private const string MerchantAccount = "shared-merchant";
    private const string ShopperKey = "shopper-reference-key-that-is-longer-than-thirty-two-bytes";

    [Fact]
    public async Task Standard_webhook_routes_shared_endpoint_to_the_payment_tenant()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangePayment(TenantId, item.MerchantReference!);

        var outcome = await fixture.Service.AcceptStandardAsync(
            new StandardWebhookRequest
            {
                NotificationItems = [new NotificationContainer { Item = item }]
            },
            CancellationToken.None);

        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
        fixture.Inbox.Verify(repository => repository.StoreAsync(
            It.Is<PaymentWebhookInbox>(webhook =>
                webhook.TenantId == TenantId &&
                webhook.NormalizedPayload.PaymentDetailId == fixture.PaymentId &&
                webhook.MerchantReference == item.MerchantReference),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Changed_tenant_routing_reference_fails_hmac_validation()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.References.TryCreate(
            OtherTenantId,
            fixture.PaymentId,
            out var changedReference);
        item.MerchantReference = changedReference;
        fixture.ArrangeProvider(OtherTenantId);

        var outcome = await fixture.Service.AcceptStandardAsync(
            new StandardWebhookRequest
            {
                NotificationItems = [new NotificationContainer { Item = item }]
            },
            CancellationToken.None);

        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
        fixture.Inbox.Verify(repository => repository.StoreAsync(
            It.IsAny<PaymentWebhookInbox>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Token_webhook_routes_using_the_signed_shopper_reference()
    {
        var fixture = new Fixture();
        var shopperReferences = new ShopperReferenceService();
        shopperReferences.TryCreate(
            TenantId,
            "actor-1",
            ShopperKey,
            out var shopperReference);
        var rawBody = $$"""
            {
              "eventId":"event-1",
              "type":"recurring.token.created",
              "createdAt":"2026-07-16T10:00:00Z",
              "data":{
                "merchantAccount":"{{MerchantAccount}}",
                "shopperReference":"{{shopperReference}}",
                "storedPaymentMethodId":"token-1",
                "type":"scheme"
              }
            }
            """;
        var signature = Convert.ToBase64String(
            HMACSHA256.HashData(
                Convert.FromHexString(fixture.WebhookKey),
                Encoding.UTF8.GetBytes(rawBody)));
        fixture.ArrangeProvider(TenantId);

        var outcome = await fixture.Service.AcceptTokenAsync(
            rawBody,
            signature,
            CancellationToken.None);

        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
        fixture.Inbox.Verify(repository => repository.StoreAsync(
            It.Is<PaymentWebhookInbox>(webhook =>
                webhook.TenantId == TenantId &&
                webhook.NormalizedPayload.ShopperReference == shopperReference),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class Fixture
    {
        public string PaymentId { get; } = Guid.NewGuid().ToString();
        public string WebhookKey { get; } = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));
        public PaymentWebhookReferenceService References { get; } = new();
        public Mock<IPaymentRepository> Payments { get; } = new();
        public Mock<IPaymentRefundRepository> Refunds { get; } = new();
        public Mock<IPaymentProviderCache> Providers { get; } = new();
        public Mock<IPaymentWebhookInboxRepository> Inbox { get; } = new();

        public Fixture()
        {
            Inbox.Setup(repository => repository.StoreAsync(
                    It.IsAny<PaymentWebhookInbox>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(WebhookStoreResult.Stored);
        }

        public PaymentWebhookIntakeService Service
        {
            get
            {
                var shopperReferences = new ShopperReferenceService();
                var resolver = new WebhookTenantResolver(
                    References,
                    new PaymentRefundWebhookReferenceService(),
                    shopperReferences);

                return new PaymentWebhookIntakeService(
                    Payments.Object,
                    Refunds.Object,
                    Providers.Object,
                    Inbox.Object,
                    new WebhookSignatureValidator(),
                    resolver,
                    new WebhookPayloadFactory(),
                    CreateOptions(),
                    Mock.Of<ILogger<PaymentWebhookIntakeService>>());
            }
        }

        private static IOptionsMonitor<PaymentOptions> CreateOptions()
        {
            var options = new Mock<IOptionsMonitor<PaymentOptions>>();
            options.SetupGet(value => value.CurrentValue)
                .Returns(new PaymentOptions());

            return options.Object;
        }

        public NotificationItem CreateStandardItem(string tenantId)
        {
            References.TryCreate(
                tenantId,
                PaymentId,
                out var reference);

            var item = new NotificationItem
            {
                PspReference = "psp-1",
                MerchantAccountCode = MerchantAccount,
                MerchantReference = reference,
                Amount = new ProviderAmount
                {
                    Value = 1050,
                    Currency = "USD"
                },
                EventCode = "AUTHORISATION",
                Success = "true"
            };
            item.AdditionalData["metadata.value_a"] = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(tenantId));

            return item;
        }

        public void SignStandard(NotificationItem item)
        {
            var canonical = string.Join(':', new[]
            {
                item.PspReference,
                string.Empty,
                item.MerchantAccountCode,
                item.MerchantReference,
                item.Amount!.Value.ToString(),
                item.Amount.Currency,
                item.EventCode,
                item.Success
            });
            item.AdditionalData["hmacSignature"] = Convert.ToBase64String(
                HMACSHA256.HashData(
                    Convert.FromHexString(WebhookKey),
                    Encoding.UTF8.GetBytes(canonical)));
        }

        public void ArrangeProvider(string tenantId)
        {
            var provider = new PaymentProvider
            {
                ProviderName = PaymentConstants.AdyenOnlineProvider,
                MerchantId = MerchantAccount,
                StandardWebhookHmacKey = WebhookKey,
                TokenWebhookHmacKey = WebhookKey
            };

            Providers.Setup(cache => cache.GetAsync(
                    tenantId,
                    PaymentConstants.AdyenOnlineProvider,
                    It.IsAny<Func<Task<PaymentProvider?>>>()))
                .ReturnsAsync(provider);
        }

        public void ArrangePayment(
            string tenantId,
            string reference)
        {
            Payments.Setup(repository => repository.GetByIdAsync(
                    tenantId,
                    PaymentId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentDetail
                {
                    ItemId = PaymentId,
                    TenantId = tenantId,
                    ProviderName = PaymentConstants.AdyenOnlineProvider,
                    InitiationRequest = new HostedCheckoutSessionRequest
                    {
                        MerchantAccount = MerchantAccount,
                        Reference = reference
                    }
                });
        }
    }
}
