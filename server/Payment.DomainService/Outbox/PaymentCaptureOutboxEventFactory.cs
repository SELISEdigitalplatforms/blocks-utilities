using Payment.DomainService.Entities;

namespace Payment.DomainService.Outbox;

public sealed class PaymentCaptureOutboxEventFactory :
    IPaymentCaptureOutboxEventFactory
{
    public PaymentOutboxEvent Create(
        PaymentDetail payment,
        PaymentCapture capture,
        string eventType,
        string captureStatus)
    {
        var eventId = Guid.NewGuid().ToString();

        return new PaymentOutboxEvent
        {
            EventId = eventId,
            EventType = eventType,
            DeduplicationKey = $"{capture.CaptureId}:{eventType}",
            Payload = new PaymentLifecycleEvent
            {
                EventId = eventId,
                EventType = eventType,
                PaymentDetailId = payment.ItemId,
                TenantId = payment.TenantId,
                OrderId = payment.OrderId,
                ProviderName = capture.ProviderName,
                PaymentStatus = captureStatus,
                Amount = payment.PreciseAmount,
                CurrencyCode = payment.CurrencyCode,
                CorrelationId = capture.CorrelationId,
                OccurredAtUtc = DateTime.UtcNow,
                CaptureId = capture.CaptureId,
                CaptureAmount = capture.Amount
            }
        };
    }
}
