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

    private delegate void TryConvertCallback(
        decimal amount,
        string currency,
        out long value);
}
