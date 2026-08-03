using Payment.DomainService.Entities;

namespace Payment.DomainService.Outbox;

public sealed class PaymentRefundOutboxEventFactory :
    IPaymentRefundOutboxEventFactory
{
    public PaymentOutboxEvent Create(
        PaymentDetail payment,
        PaymentRefund refund,
        string eventType,
        string refundStatus)
    {
        var eventId = Guid.NewGuid().ToString();

        return new PaymentOutboxEvent
        {
            EventId = eventId,
            EventType = eventType,
            DeduplicationKey =
                $"{refund.RefundId}:{eventType}",
            Payload = new PaymentLifecycleEvent
            {
                EventId = eventId,
                EventType = eventType,
                PaymentDetailId = payment.ItemId,
                TenantId = payment.TenantId,
                OrderId = payment.OrderId,
                ProviderName = refund.ProviderName,
                PaymentStatus = refundStatus,
                Amount = payment.PreciseAmount,
                CurrencyCode = payment.CurrencyCode,
                CorrelationId = refund.CorrelationId,
                OccurredAtUtc = DateTime.UtcNow,
                RefundId = refund.RefundId,
                RefundAmount = refund.Amount,
                FundReturnOperation = refund.ProviderOperation
            }
        };
    }
}
