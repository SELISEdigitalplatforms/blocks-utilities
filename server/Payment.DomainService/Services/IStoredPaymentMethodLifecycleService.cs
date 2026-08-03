using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

public interface IStoredPaymentMethodLifecycleService
{
    Task ApplyAuthorisationTokenAsync(
        PaymentWebhookInbox webhook,
        PaymentDetail payment,
        CancellationToken cancellationToken);

    Task ApplyTokenEventAsync(
        PaymentWebhookInbox webhook,
        CancellationToken cancellationToken);
}
