using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

public interface ISubscriptionPaymentProviderReadinessService
{
    /// <summary>
    /// Evaluates whether <paramref name="providerName"/> can take a subscription charge for this
    /// tenant (and, where one has its own configuration, this organization) right now, and which
    /// configuration answered.
    /// </summary>
    Task<SubscriptionPaymentProviderReadinessResult> CheckAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        CancellationToken cancellationToken);
}
