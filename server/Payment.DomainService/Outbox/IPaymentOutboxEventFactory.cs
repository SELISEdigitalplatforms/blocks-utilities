using Payment.DomainService.Entities;

namespace Payment.DomainService.Outbox;

public interface IPaymentOutboxEventFactory
{
    PaymentOutboxEvent Create(PaymentDetail payment, string eventType, string paymentStatus);
}
