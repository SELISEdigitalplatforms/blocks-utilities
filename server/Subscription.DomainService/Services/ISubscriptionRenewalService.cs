using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>Charges one subscription's renewal, or its next dunning retry.</summary>
public interface ISubscriptionRenewalService
{
    Task RenewAsync(SubscriptionDetail subscription, CancellationToken cancellationToken);

    /// <summary>
    /// Charges the overdue first period of a trial that went Unpaid for want of a card, now that
    /// one has been supplied.
    /// </summary>
    /// <remarks>
    /// Only ever called for a subscription already Unpaid — anything else reaches this by caller
    /// error, and charging a live subscription through a path meant for one that lost access would
    /// be worse than refusing. On success the subscription moves straight to Active, the same
    /// compare-and-set a renewal uses; on decline it stays exactly where it was rather than
    /// entering the dunning schedule, which is a live status.
    /// </remarks>
    Task RecoverAsync(SubscriptionDetail subscription, CancellationToken cancellationToken);
}
