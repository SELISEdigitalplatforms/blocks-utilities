using Payment.DomainService.Entities;

namespace Payment.DomainService.Outbox;

public interface IPaymentCaptureOutboxEventFactory
{
    PaymentOutboxEvent Create(
        PaymentDetail payment,
        PaymentCapture capture,
        string eventType,
        string captureStatus);
}
