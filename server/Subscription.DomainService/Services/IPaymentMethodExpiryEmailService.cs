namespace Subscription.DomainService.Services;

/// <summary>Tells billing contacts that the card their renewals charge is about to expire.</summary>
public interface IPaymentMethodExpiryEmailService
{
    /// <returns>How many notices were queued in this pass.</returns>
    Task<int> NotifyAsync(string tenantId, CancellationToken cancellationToken);
}
