using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentWebhookIntakeServiceTests
{
    private const string TenantId = "de9fc4f4baa4c4cbc829b6059b372dc6";
    private const string OtherTenantId = "***REMOVED***";
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

        var outcome = await fixture.RunStandard(item);

        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
        fixture.Inbox.Verify(repository => repository.StoreAsync(
            It.Is<PaymentWebhookInbox>(webhook =>
                webhook.TenantId == TenantId &&
                webhook.NormalizedPayload.PaymentDetailId == fixture.PaymentId &&
                webhook.MerchantReference == item.MerchantReference),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.WorkDispatcher.Verify(dispatcher =>
            dispatcher.TryDispatchAsync(
                TenantId,
                false,
                null,
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

        var outcome = await fixture.RunStandard(item);

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

        var outcome = await fixture.RunToken(rawBody, signature);

        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
        fixture.Inbox.Verify(repository => repository.StoreAsync(
            It.Is<PaymentWebhookInbox>(webhook =>
                webhook.TenantId == TenantId &&
                webhook.NormalizedPayload.ShopperReference == shopperReference),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.WorkDispatcher.Verify(dispatcher =>
            dispatcher.TryDispatchAsync(
                TenantId,
                false,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Standard_webhook_remains_accepted_when_work_cannot_be_dispatched()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangePayment(TenantId, item.MerchantReference!);
        fixture.WorkDispatcher.Setup(dispatcher =>
                dispatcher.TryDispatchAsync(
                    TenantId,
                    false,
                    null,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var outcome = await fixture.RunStandard(item);

        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
        fixture.Inbox.Verify(repository => repository.StoreAsync(
            It.IsAny<PaymentWebhookInbox>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Standard_webhook_null_items_returns_malformed()
    {
        var fixture = new Fixture();
        var outcome = await fixture.RunBody("{\"notificationItems\":null}");
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Standard_webhook_empty_items_returns_malformed()
    {
        var fixture = new Fixture();
        var outcome = await fixture.RunBody("{\"notificationItems\":[]}");
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Standard_webhook_too_many_items_returns_malformed()
    {
        var fixture = new Fixture();
        var items = Enumerable.Range(0, 101)
            .Select(_ => fixture.CreateStandardItem(TenantId))
            .ToList();
        var outcome = await fixture.RunStandard(items);
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Standard_webhook_null_inner_item_returns_malformed()
    {
        var fixture = new Fixture();
        var outcome = await fixture.RunBody("{\"notificationItems\":[{}]}");
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Standard_webhook_missing_psp_reference_returns_malformed()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        item.PspReference = "";
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Standard_webhook_missing_event_code_returns_malformed()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        item.EventCode = "";
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Standard_webhook_invalid_success_value_returns_malformed()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        item.Success = "not-a-bool";
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Standard_webhook_unresolvable_reference_returns_malformed()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        item.MerchantReference = "not-a-routing-reference";
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Standard_webhook_provider_missing_returns_not_found()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.NotFound);
    }

    [Fact]
    public async Task Standard_webhook_provider_disabled_returns_not_found()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId, provider => provider.IsEnabled = false);
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.NotFound);
    }

    [Fact]
    public async Task Standard_webhook_invalid_signature_without_refresh_returns_unauthorized()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(
            TenantId,
            provider => provider.StandardWebhookHmacKey =
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        fixture.ArrangeProviderRefresh(TenantId, null);
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Standard_webhook_signature_recovers_after_secret_refresh()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(
            TenantId,
            provider => provider.StandardWebhookHmacKey =
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        fixture.ArrangeProviderRefresh(TenantId, fixture.ValidProvider());
        fixture.ArrangePayment(TenantId, item.MerchantReference!);
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
    }

    [Fact]
    public async Task Standard_webhook_merchant_account_mismatch_returns_unauthorized()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId, provider => provider.MerchantId = "different-merchant");
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Standard_webhook_metadata_mismatch_returns_unauthorized()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        item.AdditionalData["metadata.value_a"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(OtherTenantId));
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Standard_webhook_payment_not_found_returns_not_found()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.NotFound);
    }

    [Fact]
    public async Task Standard_webhook_reference_mismatch_returns_unauthorized()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangePayment(TenantId, "different-reference");
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Standard_webhook_payment_merchant_mismatch_returns_unauthorized()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.Payments.Setup(repository => repository.GetByIdAsync(
                TenantId, fixture.PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = fixture.PaymentId,
                TenantId = TenantId,
                ProviderName = PaymentConstants.AdyenOnlineProvider,
                InitiationRequest = new ProviderInitiationRequest
                {
                    MerchantAccount = "other-merchant",
                    Reference = item.MerchantReference
                }
            });
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Standard_webhook_provider_name_mismatch_returns_unauthorized()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.Payments.Setup(repository => repository.GetByIdAsync(
                TenantId, fixture.PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = fixture.PaymentId,
                TenantId = TenantId,
                ProviderName = "some-other-provider",
                InitiationRequest = new ProviderInitiationRequest
                {
                    MerchantAccount = MerchantAccount,
                    Reference = item.MerchantReference
                }
            });
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Standard_webhook_duplicate_store_is_still_accepted()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangePayment(TenantId, item.MerchantReference!);
        fixture.Inbox.Setup(repository => repository.StoreAsync(
                It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookStoreResult.Duplicate);
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
    }

    [Fact]
    public async Task Standard_webhook_storage_failure_returns_storage_unavailable()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangePayment(TenantId, item.MerchantReference!);
        fixture.Inbox.Setup(repository => repository.StoreAsync(
                It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.StorageUnavailable);
    }

    [Fact]
    public async Task Standard_webhook_timeout_returns_storage_unavailable()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangePayment(TenantId, item.MerchantReference!);
        fixture.Inbox.Setup(repository => repository.StoreAsync(
                It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.StorageUnavailable);
    }

    [Fact]
    public async Task Standard_webhook_rethrows_when_application_is_stopping()
    {
        var fixture = new Fixture();
        var item = fixture.CreateStandardItem(TenantId);
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangePayment(TenantId, item.MerchantReference!);
        fixture.Inbox.Setup(repository => repository.StoreAsync(
                It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => fixture.Service.AcceptAsync(
            PaymentConstants.AdyenOnlineProvider,
            fixture.StandardBody(item),
            new Dictionary<string, string>(),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Standard_refund_webhook_is_accepted()
    {
        var fixture = new Fixture();
        var refundId = Guid.NewGuid().ToString();
        var refundReference = fixture.CreateRefundReference(TenantId, refundId);
        var item = fixture.CreateStandardItem(TenantId);
        item.MerchantReference = refundReference;
        item.EventCode = "REFUND";
        item.OriginalReference = "original-psp";
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangeRefundPayment(TenantId, new PaymentDetail
        {
            ItemId = fixture.PaymentId,
            TenantId = TenantId,
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            Refunds =
            [
                new PaymentRefund
                {
                    RefundId = refundId,
                    ProviderReference = refundReference,
                    ProviderMerchantAccount = MerchantAccount,
                    OriginalPaymentPspReference = "original-psp"
                }
            ]
        });
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
    }

    [Fact]
    public async Task Standard_refund_reference_with_non_refund_event_returns_unauthorized()
    {
        var fixture = new Fixture();
        var refundId = Guid.NewGuid().ToString();
        var refundReference = fixture.CreateRefundReference(TenantId, refundId);
        var item = fixture.CreateStandardItem(TenantId);
        item.MerchantReference = refundReference;
        item.EventCode = "AUTHORISATION";
        item.OriginalReference = "original-psp";
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangeRefundPayment(TenantId, new PaymentDetail
        {
            ItemId = fixture.PaymentId,
            TenantId = TenantId,
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            Refunds =
            [
                new PaymentRefund
                {
                    RefundId = refundId,
                    ProviderReference = refundReference,
                    ProviderMerchantAccount = MerchantAccount,
                    OriginalPaymentPspReference = "original-psp"
                }
            ]
        });
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Standard_refund_original_reference_mismatch_returns_unauthorized()
    {
        var fixture = new Fixture();
        var refundId = Guid.NewGuid().ToString();
        var refundReference = fixture.CreateRefundReference(TenantId, refundId);
        var item = fixture.CreateStandardItem(TenantId);
        item.MerchantReference = refundReference;
        item.EventCode = "REFUND";
        item.OriginalReference = "original-psp";
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangeRefundPayment(TenantId, new PaymentDetail
        {
            ItemId = fixture.PaymentId,
            TenantId = TenantId,
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            Refunds =
            [
                new PaymentRefund
                {
                    RefundId = refundId,
                    ProviderReference = refundReference,
                    ProviderMerchantAccount = MerchantAccount,
                    OriginalPaymentPspReference = "a-different-original"
                }
            ]
        });
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Standard_refund_payment_not_found_returns_not_found()
    {
        var fixture = new Fixture();
        var refundId = Guid.NewGuid().ToString();
        var refundReference = fixture.CreateRefundReference(TenantId, refundId);
        var item = fixture.CreateStandardItem(TenantId);
        item.MerchantReference = refundReference;
        item.EventCode = "REFUND";
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.NotFound);
    }

    [Fact]
    public async Task Standard_capture_webhook_is_accepted()
    {
        var fixture = new Fixture();
        var captureId = Guid.NewGuid().ToString();
        var captureReference = fixture.CreateCaptureReference(TenantId, captureId);
        var item = fixture.CreateStandardItem(TenantId);
        item.MerchantReference = captureReference;
        item.EventCode = "CAPTURE";
        item.OriginalReference = "original-psp";
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangeCapturePayment(TenantId, new PaymentDetail
        {
            ItemId = fixture.PaymentId,
            TenantId = TenantId,
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            Captures =
            [
                new PaymentCapture
                {
                    CaptureId = captureId,
                    ProviderReference = captureReference,
                    ProviderMerchantAccount = MerchantAccount,
                    OriginalPaymentPspReference = "original-psp"
                }
            ]
        });
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
    }

    [Fact]
    public async Task Standard_capture_reference_with_non_capture_event_returns_unauthorized()
    {
        var fixture = new Fixture();
        var captureId = Guid.NewGuid().ToString();
        var captureReference = fixture.CreateCaptureReference(TenantId, captureId);
        var item = fixture.CreateStandardItem(TenantId);
        item.MerchantReference = captureReference;
        item.EventCode = "AUTHORISATION";
        item.OriginalReference = "original-psp";
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangeCapturePayment(TenantId, new PaymentDetail
        {
            ItemId = fixture.PaymentId,
            TenantId = TenantId,
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            Captures =
            [
                new PaymentCapture
                {
                    CaptureId = captureId,
                    ProviderReference = captureReference,
                    ProviderMerchantAccount = MerchantAccount,
                    OriginalPaymentPspReference = "original-psp"
                }
            ]
        });
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Standard_capture_original_reference_mismatch_returns_unauthorized()
    {
        var fixture = new Fixture();
        var captureId = Guid.NewGuid().ToString();
        var captureReference = fixture.CreateCaptureReference(TenantId, captureId);
        var item = fixture.CreateStandardItem(TenantId);
        item.MerchantReference = captureReference;
        item.EventCode = "CAPTURE";
        item.OriginalReference = "original-psp";
        fixture.SignStandard(item);
        fixture.ArrangeProvider(TenantId);
        fixture.ArrangeCapturePayment(TenantId, new PaymentDetail
        {
            ItemId = fixture.PaymentId,
            TenantId = TenantId,
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            Captures =
            [
                new PaymentCapture
                {
                    CaptureId = captureId,
                    ProviderReference = captureReference,
                    ProviderMerchantAccount = MerchantAccount,
                    OriginalPaymentPspReference = "a-different-original"
                }
            ]
        });
        var outcome = await fixture.RunStandard(item);
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Token_webhook_empty_body_returns_malformed()
    {
        var fixture = new Fixture();
        var outcome = await fixture.RunToken(string.Empty, "signature");
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Token_webhook_empty_signature_returns_malformed()
    {
        var fixture = new Fixture();
        var outcome = await fixture.RunToken("{}", string.Empty);
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Token_webhook_invalid_json_returns_malformed()
    {
        var fixture = new Fixture();
        var outcome = await fixture.RunToken("{not-valid-json", "signature");
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Token_webhook_unknown_event_type_returns_malformed()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        var body = fixture.BuildTokenBody(
            "recurring.token.unknown", MerchantAccount, shopperReference, "token-1");
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Token_webhook_missing_event_id_returns_malformed()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, shopperReference, "token-1",
            includeEventId: false);
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Token_webhook_data_not_object_returns_malformed()
    {
        var fixture = new Fixture();
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, "x", "token-1",
            dataAsObject: false);
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Token_webhook_unresolvable_shopper_returns_malformed()
    {
        var fixture = new Fixture();
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, "unknown-shopper", "token-1");
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.Malformed);
    }

    [Fact]
    public async Task Token_webhook_provider_missing_returns_not_found()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, shopperReference, "token-1");
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.NotFound);
    }

    [Fact]
    public async Task Token_webhook_provider_disabled_returns_not_found()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        fixture.ArrangeProvider(TenantId, provider => provider.IsEnabled = false);
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, shopperReference, "token-1");
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.NotFound);
    }

    [Fact]
    public async Task Token_webhook_invalid_signature_without_refresh_returns_unauthorized()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        fixture.ArrangeProvider(
            TenantId,
            provider => provider.TokenWebhookHmacKey =
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        fixture.ArrangeProviderRefresh(TenantId, null);
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, shopperReference, "token-1");
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Token_webhook_signature_recovers_after_secret_refresh()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        fixture.ArrangeProvider(
            TenantId,
            provider => provider.TokenWebhookHmacKey =
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        fixture.ArrangeProviderRefresh(TenantId, fixture.ValidProvider());
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, shopperReference, "token-1");
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.Accepted);
    }

    [Fact]
    public async Task Token_webhook_merchant_mismatch_returns_unauthorized()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        fixture.ArrangeProvider(TenantId);
        var body = fixture.BuildTokenBody(
            "recurring.token.created", "different-merchant", shopperReference, "token-1");
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Token_webhook_missing_stored_method_returns_unauthorized()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        fixture.ArrangeProvider(TenantId);
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, shopperReference, string.Empty);
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.Unauthorized);
    }

    [Fact]
    public async Task Token_webhook_storage_failure_returns_storage_unavailable()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        fixture.ArrangeProvider(TenantId);
        fixture.Inbox.Setup(repository => repository.StoreAsync(
                It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, shopperReference, "token-1");
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.StorageUnavailable);
    }

    [Fact]
    public async Task Token_webhook_timeout_returns_storage_unavailable()
    {
        var fixture = new Fixture();
        var shopperReference = fixture.CreateShopperReference(TenantId);
        fixture.ArrangeProvider(TenantId);
        fixture.Inbox.Setup(repository => repository.StoreAsync(
                It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var body = fixture.BuildTokenBody(
            "recurring.token.created", MerchantAccount, shopperReference, "token-1");
        var outcome = await fixture.RunToken(body, fixture.SignToken(body));
        outcome.Should().Be(WebhookIntakeOutcome.StorageUnavailable);
    }

    private sealed class Fixture
    {
        public string PaymentId { get; } = Guid.NewGuid().ToString();
        public string WebhookKey { get; } = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));
        public PaymentWebhookReferenceService References { get; } = new();
        public Mock<IPaymentRepository> Payments { get; } = new();
        public Mock<IPaymentRefundRepository> Refunds { get; } = new();
        public Mock<IPaymentCaptureRepository> Captures { get; } = new();
        public Mock<IPaymentProviderCache> Providers { get; } = new();
        public Mock<IPaymentWebhookInboxRepository> Inbox { get; } = new();
        public Mock<IPaymentWorkDispatcher> WorkDispatcher { get; } = new();

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
                    new PaymentCaptureWebhookReferenceService(),
                    shopperReferences);

                return new PaymentWebhookIntakeService(
                    Payments.Object,
                    Refunds.Object,
                    Captures.Object,
                    Providers.Object,
                    Inbox.Object,
                    new WebhookNormalizerResolver(
                    [
                        new AdyenWebhookNormalizer(new ProviderFailureReasonMapper())
                    ]),
                    new WebhookSignatureVerifierResolver(
                    [
                        new AdyenWebhookSignatureVerifier()
                    ]),
                    resolver,
                    WorkDispatcher.Object,
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
                item.OriginalReference ?? string.Empty,
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

        public PaymentProvider ArrangeProvider(
            string tenantId,
            Action<PaymentProvider>? configure = null)
        {
            var provider = new PaymentProvider
            {
                ProviderName = PaymentConstants.AdyenOnlineProvider,
                MerchantId = MerchantAccount,
                StandardWebhookHmacKey = WebhookKey,
                TokenWebhookHmacKey = WebhookKey
            };

            configure?.Invoke(provider);

            Providers.Setup(cache => cache.GetAsync(
                    tenantId,
                    PaymentConstants.AdyenOnlineProvider,
                    It.IsAny<Func<Task<PaymentProvider?>>>()))
                .ReturnsAsync(provider);

            return provider;
        }

        public void ArrangeProviderRefresh(
            string tenantId,
            PaymentProvider? provider)
        {
            Providers.Setup(cache => cache.RefreshAsync(
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
                    InitiationRequest = new ProviderInitiationRequest
                    {
                        MerchantAccount = MerchantAccount,
                        Reference = reference
                    }
                });
        }

        public string CreateRefundReference(string tenantId, string refundId)
        {
            new PaymentRefundWebhookReferenceService()
                .TryCreate(tenantId, refundId, out var reference);
            return reference;
        }

        public string CreateCaptureReference(string tenantId, string captureId)
        {
            new PaymentCaptureWebhookReferenceService()
                .TryCreate(tenantId, captureId, out var reference);
            return reference;
        }

        public void ArrangeRefundPayment(
            string tenantId,
            PaymentDetail payment)
        {
            Refunds.Setup(repository => repository.GetPaymentByRefundIdAsync(
                    tenantId,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(payment);
        }

        public void ArrangeCapturePayment(
            string tenantId,
            PaymentDetail payment)
        {
            Captures.Setup(repository => repository.GetPaymentByCaptureIdAsync(
                    tenantId,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(payment);
        }

        public string BuildTokenBody(
            string type,
            string merchantAccount,
            string shopperReference,
            string storedPaymentMethodId,
            bool dataAsObject = true,
            bool includeEventId = true)
        {
            var eventLine = includeEventId
                ? "\"eventId\":\"event-1\","
                : string.Empty;
            var data = dataAsObject
                ? $$"""
                    {
                        "merchantAccount":"{{merchantAccount}}",
                        "shopperReference":"{{shopperReference}}",
                        "storedPaymentMethodId":"{{storedPaymentMethodId}}",
                        "type":"scheme"
                    }
                    """
                : "\"not-an-object\"";

            return $$"""
                {
                  {{eventLine}}
                  "type":"{{type}}",
                  "createdAt":"2026-07-16T10:00:00Z",
                  "data":{{data}}
                }
                """;
        }

        public string SignToken(string rawBody) => Convert.ToBase64String(
            HMACSHA256.HashData(
                Convert.FromHexString(WebhookKey),
                Encoding.UTF8.GetBytes(rawBody)));

        private static readonly IReadOnlyDictionary<string, string> NoHeaders =
            new Dictionary<string, string>();

        public string StandardBody(params NotificationItem[] items) =>
            JsonSerializer.Serialize(
                new StandardWebhookRequest
                {
                    NotificationItems = items
                        .Select(item => new NotificationContainer { Item = item })
                        .ToList()
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        public Task<WebhookIntakeOutcome> RunStandard(NotificationItem item) =>
            RunBody(StandardBody(item), NoHeaders);

        public Task<WebhookIntakeOutcome> RunStandard(IEnumerable<NotificationItem> items) =>
            RunBody(StandardBody([.. items]), NoHeaders);

        public Task<WebhookIntakeOutcome> RunToken(
            string rawBody,
            string? signature = null,
            string? protocol = null)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (signature != null) headers["hmacsignature"] = signature;
            if (protocol != null) headers["protocol"] = protocol;

            return RunBody(rawBody, headers);
        }

        public Task<WebhookIntakeOutcome> RunBody(string rawBody) =>
            RunBody(rawBody, NoHeaders);

        public Task<WebhookIntakeOutcome> RunBody(
            string rawBody,
            IReadOnlyDictionary<string, string> headers) =>
            Service.AcceptAsync(
                PaymentConstants.AdyenOnlineProvider,
                rawBody,
                headers,
                CancellationToken.None);

        public PaymentProvider ValidProvider() => new()
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            MerchantId = MerchantAccount,
            StandardWebhookHmacKey = WebhookKey,
            TokenWebhookHmacKey = WebhookKey
        };

        public string CreateShopperReference(string tenantId)
        {
            var shopperReferences = new ShopperReferenceService();
            shopperReferences.TryCreate(
                tenantId,
                "actor-1",
                ShopperKey,
                out var shopperReference);
            return shopperReference;
        }
    }
}
