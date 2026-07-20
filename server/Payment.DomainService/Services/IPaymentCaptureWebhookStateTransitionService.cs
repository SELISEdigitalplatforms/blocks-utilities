using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

public interface IPaymentCaptureWebhookStateTransitionService
{
    Task ApplyAsync(
        PaymentWebhookInbox webhook,
        CancellationToken cancellationToken);
}
