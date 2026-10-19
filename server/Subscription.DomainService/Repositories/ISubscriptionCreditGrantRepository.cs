using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>The credit grant ledger. See <see cref="SubscriptionCreditGrant"/>.</summary>
public interface ISubscriptionCreditGrantRepository
{
    /// <summary>
    /// Writes a new grant. False when one with the same id already exists, which is how finishing
    /// a purchase twice — a retry, or the recovery sweep after a crash — grants its credits once.
    /// </summary>
    Task<bool> TryInsertAsync(SubscriptionCreditGrant grant, CancellationToken cancellationToken);

    Task<SubscriptionCreditGrant?> GetAsync(
        string tenantId,
        string grantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every grant in one bucket that can still be spent at <paramref name="nowUtc"/>, in the order
    /// they are consumed: soonest expiry first, grants that do not expire last, then oldest first.
    /// </summary>
    Task<IReadOnlyList<SubscriptionCreditGrant>> ListLiveAsync(
        string tenantId,
        string subscriptionId,
        string meterKey,
        int? seatNumber,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// A subscription's grants for the history list, newest first. Each filter is optional; a null
    /// seat filter with <paramref name="poolOnly"/> false means every bucket.
    /// </summary>
    Task<IReadOnlyList<SubscriptionCreditGrant>> ListAsync(
        string tenantId,
        string subscriptionId,
        string? meterKey,
        int? seatNumber,
        bool poolOnly,
        CreditGrantStatus? status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes <paramref name="quantity"/> from one grant, only if it still holds that much and can
    /// still be spent. Returns the grant after the take, or null when it could not be taken — spent
    /// by somebody else, expired or closed — in which case nothing changed.
    /// </summary>
    /// <remarks>
    /// A grant taken to zero is marked exhausted by a second conditional write. Between the two it
    /// reads as active with nothing remaining, which every reader already treats as not live.
    /// </remarks>
    Task<SubscriptionCreditGrant?> TryConsumeAsync(
        string tenantId,
        string grantId,
        decimal quantity,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Active grants the maintenance sweep owes a look: past their expiry, or not checked since
    /// <paramref name="checkedBeforeUtc"/>.
    /// </summary>
    Task<IReadOnlyList<SubscriptionCreditGrant>> ListDueForCheckAsync(
        string tenantId,
        DateTime nowUtc,
        DateTime checkedBeforeUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Closes an active grant as expired or forfeited. False when it was no longer active.</summary>
    Task<bool> TryCloseAsync(
        string tenantId,
        string grantId,
        CreditGrantStatus status,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Records that the sweep looked at these grants and found them still live.</summary>
    Task MarkCheckedAsync(
        string tenantId,
        IReadOnlyCollection<string> grantIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives back what <see cref="TryConsumeAsync"/> took, when the recording it was for is refused.
    /// Reopens an exhausted grant. Does nothing to one that has since expired or been forfeited.
    /// </summary>
    Task<bool> RestoreAsync(
        string tenantId,
        string grantId,
        decimal quantity,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
