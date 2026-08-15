namespace Subscription.DomainService.Outbox;

/// <summary>Closes usage periods that have ended and invoices their overage.</summary>
public interface ISubscriptionUsageRatingProcessor
{
    /// <returns>How many usage periods were closed out, across every subscription swept.</returns>
    Task<int> CloseDuePeriodsAsync(string tenantId, CancellationToken cancellationToken);
}
