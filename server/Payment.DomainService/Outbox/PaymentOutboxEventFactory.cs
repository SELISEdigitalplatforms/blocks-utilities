using Payment.DomainService.Entities;

namespace Payment.DomainService.Outbox;

public sealed class PaymentOutboxEventFactory : IPaymentOutboxEventFactory
{
    public PaymentOutboxEvent Create(PaymentDetail payment, string eventType, string paymentStatus)
    {
        var eventId = Guid.NewGuid().ToString();
        var payload = new PaymentLifecycleEvent
        {
            EventId = eventId,
            EventType = eventType,
            PaymentDetailId = payment.ItemId,
            TenantId = payment.TenantId,
            OrderId = payment.OrderId,
            ProviderName = payment.ProviderName,
            PaymentStatus = paymentStatus,
            Amount = payment.PreciseAmount,
            CurrencyCode = payment.CurrencyCode,
            CorrelationId = payment.CorrelationId,
            OccurredAtUtc = DateTime.UtcNow
        };

        return new PaymentOutboxEvent
        {
            EventId = eventId,
            EventType = eventType,
            DeduplicationKey = $"{payment.ItemId}:{eventType}",
            Payload = payload
        };
    }
}
