using Payment.DomainService.Entities;

namespace Payment.DomainService.Outbox;

public interface IPaymentRefundOutboxEventFactory
{
    PaymentOutboxEvent Create(
        PaymentDetail payment,
        PaymentRefund refund,
        string eventType,
        string refundStatus);
}
