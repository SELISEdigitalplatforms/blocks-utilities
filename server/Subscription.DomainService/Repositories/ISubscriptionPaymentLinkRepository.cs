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

    /// <summary>
    /// The newest card-collection link of one subscription, or null when it has never opened one.
    /// </summary>
    /// <remarks>
    /// Newest by <see cref="SubscriptionPaymentLink.CreatedAtUtc"/>, ties broken by item id, so
    /// the answer is deterministic once a completed setup and its replacement coexist. An
    /// unordered first match could hand back the completed one and open a third live session.
    /// </remarks>
    Task<SubscriptionPaymentLink?> FindLatestSetupLinkAsync(
        string tenantId,
        string subscriptionId,
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
