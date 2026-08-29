using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>What a reservation attempt found.</summary>
public enum CampaignReservationOutcome
{
    /// <summary>A new reservation was created for this subscription.</summary>
    Reserved,

    /// <summary>
    /// This subscription already held the reservation -- a retry landed here, either because the
    /// caller genuinely retried or because two concurrent attempts for the same subscription
    /// raced and both need to see success.
    /// </summary>
    AlreadyReservedBySameSubscription,

    /// <summary>
    /// A different subscription holds this campaign for this organization, and the campaign is
    /// one-use. Nothing was written.
    /// </summary>
    HeldByAnotherSubscription
}

public interface ICampaignRedemptionRepository
{
    /// <summary>
    /// Atomically claims a campaign for a subscription, or reports who already holds it.
    /// </summary>
    /// <remarks>
    /// Safe under concurrency by construction, not by a check-then-insert this method happens to
    /// order carefully: two callers racing for the same one-use campaign either both land on
    /// <see cref="CampaignReservationOutcome.Reserved"/> for the same subscription id (the same
    /// logical attempt, retried) or exactly one gets <see cref="CampaignReservationOutcome.Reserved"/>
    /// and the other gets <see cref="CampaignReservationOutcome.HeldByAnotherSubscription"/> --
    /// there is no interleaving that lets both through. A non-one-use campaign never returns
    /// <see cref="CampaignReservationOutcome.HeldByAnotherSubscription"/>: every subscription gets
    /// its own row.
    /// </remarks>
    Task<CampaignReservationOutcome> TryReserveAsync(
        CampaignRedemption reservation, CancellationToken cancellationToken);

    /// <summary>
    /// Reserved -> Redeemed. Idempotent: called again for a subscription already Redeemed
    /// succeeds without writing anything, the same way a repeated payment-provider webhook must
    /// never be told its second delivery failed.
    /// </summary>
    Task TryMarkRedeemedAsync(
        string tenantId,
        string discountId,
        string subscriptionId,
        DateTime redeemedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reserved -> ReleasePending -> Released, in one call. Idempotent, and a no-op on a
    /// subscription that already reached <see cref="Enums.CampaignRedemptionState.Redeemed"/> --
    /// once activated, a campaign is never released by a later cancellation.
    /// </summary>
    /// <remarks>
    /// The intermediate <see cref="Enums.CampaignRedemptionState.ReleasePending"/> is written and
    /// committed before the second step is attempted, so a crash between the two leaves a durable,
    /// specific state for a reconciliation sweep to find and complete -- rather than leaving the
    /// row at <see cref="Enums.CampaignRedemptionState.Reserved"/>, indistinguishable from a
    /// reservation nothing has happened to yet.
    /// </remarks>
    Task TryReleaseAsync(
        string tenantId,
        string discountId,
        string subscriptionId,
        DateTime releasedAtUtc,
        CancellationToken cancellationToken);

    Task<CampaignRedemption?> FindAsync(
        string tenantId, string discountId, string subscriptionId, CancellationToken cancellationToken);

    /// <summary>
    /// Redemptions still at <see cref="Enums.CampaignRedemptionState.Reserved"/> or
    /// <see cref="Enums.CampaignRedemptionState.ReleasePending"/>, reserved before
    /// <paramref name="reservedBeforeUtc"/> -- the ones a reconciliation sweep exists to find.
    /// </summary>
    /// <remarks>
    /// Ordered oldest first, so a sweep capped at <paramref name="limit"/> makes steady progress
    /// through a large backlog rather than repeatedly finding the same page.
    /// </remarks>
    Task<IReadOnlyList<CampaignRedemption>> ListStaleAsync(
        string tenantId, DateTime reservedBeforeUtc, int limit, CancellationToken cancellationToken);
}
