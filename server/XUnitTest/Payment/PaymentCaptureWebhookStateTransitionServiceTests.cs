using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentCaptureWebhookStateTransitionServiceTests
{
    [Fact]
    public async Task Successful_capture_updates_amount_and_payment_status()
    {
        const string tenantId = "tenant";
        var capture = new PaymentCapture
        {
            CaptureId = Guid.NewGuid().ToString(),
            Status = PaymentCaptureStatuses.Submitted,
            Amount = 10,
            CurrencyCode = "EUR",
            OriginalPaymentPspReference = "original",
            ProviderName = "provider"
        };
        var payment = new PaymentDetail
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            AuthorizedAmount = 10,
            CurrencyCode = "EUR",
            Captures = [capture]
        };
        var repository = new Mock<IPaymentCaptureRepository>();
        repository.Setup(item => item.GetPaymentByCaptureIdAsync(
                tenantId,
                capture.CaptureId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        repository.Setup(item => item.ApplyProviderEventAsync(
                tenantId,
                payment.ItemId,
                capture.CaptureId,
                It.IsAny<IReadOnlyCollection<string>>(),
                PaymentCaptureStatuses.Succeeded,
                PaymentStatuses.Captured,
                "capture-psp",
                It.IsAny<DateTime>(),
                -10,
                10,
                null,
                It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var minorUnits = new Mock<ICurrencyMinorUnitResolver>();
        minorUnits.Setup(item => item.TryConvert(
                10,
                "EUR",
                out It.Ref<long>.IsAny))
            .Callback(new TryConvertCallback(
                (decimal _, string _, out long value) => value = 1000))
            .Returns(true);
        var service = new PaymentCaptureWebhookStateTransitionService(
            repository.Object,
            minorUnits.Object,
            new PaymentCaptureOutboxEventFactory(),
            NullLogger<PaymentCaptureWebhookStateTransitionService>.Instance);
        var webhook = new PaymentWebhookInbox
        {
            TenantId = tenantId,
            EventCode = "CAPTURE",
            EventDateUtc = DateTime.UtcNow,
            NormalizedPayload = new PaymentWebhookPayload
            {
                PaymentDetailId = payment.ItemId,
                CaptureId = capture.CaptureId,
                PspReference = "capture-psp",
                OriginalPspReference = "original",
                Success = true,
                AmountMinorUnits = 1000,
                CurrencyCode = "EUR"
            }
        };

        await service.ApplyAsync(webhook, CancellationToken.None);

        repository.VerifyAll();
    }

    [Fact]
    public async Task Incomplete_capture_payload_is_rejected()
    {
        var (service, webhook) = Scenario();
        webhook.NormalizedPayload.PspReference = null;

        var act = () => service.ApplyAsync(webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// A capture made in the provider's own dashboard names no capture of ours, because this
    /// service never made one. Demanding a capture id threw, the event dead-lettered, and the
    /// payment stayed authorised while the money had moved.
    /// </summary>
    [Fact]
    public async Task An_externally_made_capture_is_applied_to_the_payment()
    {
        var harness = new ExternalHarness();

        await harness.Service.ApplyAsync(harness.Webhook, CancellationToken.None);

        harness.Repository.Verify(item => item.ApplyExternalCaptureAsync(
                "tenant",
                "payment-1",
                PaymentStatuses.Captured,
                10,
                "capture-psp",
                It.IsAny<DateTime>(),
                It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_externally_made_partial_capture_leaves_the_payment_partially_captured()
    {
        var harness = new ExternalHarness(authorizedAmount: 25);

        await harness.Service.ApplyAsync(harness.Webhook, CancellationToken.None);

        harness.Repository.Verify(item => item.ApplyExternalCaptureAsync(
                "tenant",
                "payment-1",
                PaymentStatuses.PartiallyCaptured,
                10,
                "capture-psp",
                It.IsAny<DateTime>(),
                It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Nothing was captured and there is no capture record to fail, so the payment is left
    /// alone rather than the event being thrown away and retried.
    /// </summary>
    [Fact]
    public async Task An_externally_failed_capture_changes_nothing()
    {
        var harness = new ExternalHarness();
        harness.Webhook.NormalizedPayload.Success = false;

        await harness.Service.ApplyAsync(harness.Webhook, CancellationToken.None);

        harness.Repository.Verify(item => item.ApplyExternalCaptureAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_externally_made_capture_for_another_payment_is_rejected()
    {
        var harness = new ExternalHarness();
        harness.Webhook.NormalizedPayload.OriginalPspReference = "someone-elses";

        var act = () => harness.Service.ApplyAsync(harness.Webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class ExternalHarness
    {
        public Mock<IPaymentCaptureRepository> Repository { get; } = new();
        public PaymentCaptureWebhookStateTransitionService Service { get; }
        public PaymentWebhookInbox Webhook { get; }

        public ExternalHarness(decimal authorizedAmount = 10)
        {
            Repository.Setup(item => item.GetPaymentAsync(
                    "tenant", "payment-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentDetail
                {
                    ItemId = "payment-1",
                    TenantId = "tenant",
                    AuthorizedAmount = authorizedAmount,
                    CurrencyCode = "EUR",
                    PspReference = "original",
                    ProviderName = "provider"
                });
            Repository.Setup(item => item.ApplyExternalCaptureAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                    It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var minorUnits = new Mock<ICurrencyMinorUnitResolver>();
            minorUnits.Setup(item => item.TryConvertBack(1000, "EUR", out It.Ref<decimal>.IsAny))
                .Callback(new TryConvertBackCallback(
                    (long _, string _, out decimal value) => value = 10))
                .Returns(true);

            Service = new PaymentCaptureWebhookStateTransitionService(
                Repository.Object,
                minorUnits.Object,
                new PaymentCaptureOutboxEventFactory(),
                NullLogger<PaymentCaptureWebhookStateTransitionService>.Instance);

            Webhook = new PaymentWebhookInbox
            {
                TenantId = "tenant",
                EventCode = "CAPTURE",
                EventDateUtc = DateTime.UtcNow,
                NormalizedPayload = new PaymentWebhookPayload
                {
                    PaymentDetailId = "payment-1",
                    // No capture id: this service never made this capture.
                    CaptureId = null,
                    PspReference = "capture-psp",
                    OriginalPspReference = "original",
                    Success = true,
                    AmountMinorUnits = 1000,
                    CurrencyCode = "EUR"
                }
            };
        }
    }

    private delegate void TryConvertBackCallback(
        long minorUnits,
        string currencyCode,
        out decimal amount);

    [Fact]
    public async Task Unknown_capture_reference_is_rejected()
    {
        var (service, webhook) = Scenario();
        webhook.NormalizedPayload.PaymentDetailId = "mismatched-payment";

        var act = () => service.ApplyAsync(webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Mismatched_original_reference_is_rejected()
    {
        var (service, webhook) = Scenario();
        webhook.NormalizedPayload.OriginalPspReference = "different-original";

        var act = () => service.ApplyAsync(webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Mismatched_capture_amount_is_rejected()
    {
        var (service, webhook) = Scenario();
        webhook.NormalizedPayload.AmountMinorUnits = 999;

        var act = () => service.ApplyAsync(webhook, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static (PaymentCaptureWebhookStateTransitionService Service, PaymentWebhookInbox Webhook)
        Scenario()
    {
        const string tenantId = "tenant";
        var capture = new PaymentCapture
        {
            CaptureId = "capture-1",
            Status = PaymentCaptureStatuses.Submitted,
            Amount = 10,
            CurrencyCode = "EUR",
            OriginalPaymentPspReference = "original",
            ProviderName = "provider"
        };
        var payment = new PaymentDetail
        {
            ItemId = "payment-1",
            TenantId = tenantId,
            AuthorizedAmount = 10,
            CurrencyCode = "EUR",
            Captures = [capture]
        };
        var repository = new Mock<IPaymentCaptureRepository>();
        repository.Setup(item => item.GetPaymentByCaptureIdAsync(
                tenantId, capture.CaptureId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        var minorUnits = new Mock<ICurrencyMinorUnitResolver>();
        minorUnits.Setup(item => item.TryConvert(10, "EUR", out It.Ref<long>.IsAny))
            .Callback(new TryConvertCallback(
                (decimal _, string _, out long value) => value = 1000))
            .Returns(true);
        var service = new PaymentCaptureWebhookStateTransitionService(
            repository.Object,
            minorUnits.Object,
            new PaymentCaptureOutboxEventFactory(),
            NullLogger<PaymentCaptureWebhookStateTransitionService>.Instance);
        var webhook = new PaymentWebhookInbox
        {
            TenantId = tenantId,
            EventCode = "CAPTURE",
            EventDateUtc = DateTime.UtcNow,
            NormalizedPayload = new PaymentWebhookPayload
            {
                PaymentDetailId = payment.ItemId,
                CaptureId = capture.CaptureId,
                PspReference = "capture-psp",
                OriginalPspReference = "original",
                Success = true,
                AmountMinorUnits = 1000,
                CurrencyCode = "EUR"
            }
        };
        return (service, webhook);
    }

    private delegate void TryConvertCallback(
        decimal amount,
        string currency,
        out long value);
}
