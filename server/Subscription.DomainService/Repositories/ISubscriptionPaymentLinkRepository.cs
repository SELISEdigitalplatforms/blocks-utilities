using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionPaymentLinkRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    Task<bool> TryCreateAsync(
        SubscriptionPaymentLink link,
        CancellationToken cancellationToken);

    Task<SubscriptionPaymentLink?> FindByPaymentAsync(
        string tenantId,
        string paymentDetailId,
        CancellationToken cancellationToken);

    Task<SubscriptionPaymentLink?> FindBySubscriptionAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken);

    /// <summary>Pending links the activation sweep should look at now.</summary>
    Task<IReadOnlyList<SubscriptionPaymentLink>> ListDueAsync(
        string tenantId,
        DateTime dueAtUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Settles a link, but only if it is still pending — so a sweep that runs twice, or two
    /// workers that both pick it up, apply the outcome once.
    /// </summary>
    Task<bool> TrySettleAsync(
        string tenantId,
        string linkId,
        SubscriptionPaymentLinkState state,
        CancellationToken cancellationToken);

    Task RescheduleAsync(
        string tenantId,
        string linkId,
        int attemptCount,
        DateTime nextCheckAtUtc,
        string? failureReason,
        CancellationToken cancellationToken);
}
