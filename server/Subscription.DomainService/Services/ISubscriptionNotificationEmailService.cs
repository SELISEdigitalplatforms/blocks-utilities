using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Tells a subscription's billing contact that its seats, plan or cancellation changed.
/// </summary>
public interface ISubscriptionNotificationEmailService
{
    /// <summary>
    /// Sends the email an event calls for, if it calls for one; every other event is ignored.
    /// </summary>
    Task SendAsync(
        SubscriptionLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken);
}
