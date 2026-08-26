using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

/// <summary>
/// Applies an event that reports a card being stored, or the attempt to store one ending.
/// </summary>
public interface IPaymentMethodSetupWebhookStateTransitionService
{
    Task ApplyAsync(PaymentWebhookInbox webhook, CancellationToken cancellationToken);
}
