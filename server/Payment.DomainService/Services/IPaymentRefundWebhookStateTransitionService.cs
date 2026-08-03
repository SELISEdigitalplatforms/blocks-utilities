using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

public interface IPaymentRefundWebhookStateTransitionService
{
    Task ApplyAsync(
        PaymentWebhookInbox webhook,
        CancellationToken cancellationToken);
}
