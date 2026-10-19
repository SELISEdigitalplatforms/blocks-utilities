using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A quantity of one meter's credits held by a subscription, on top of what its plan includes.
/// </summary>
/// <remarks>
/// One document per purchase, free grant or move, each with its own remaining balance and
/// expiry, so a subscriber can see exactly what they bought and what is left of it. Consumed only
/// after the plan's own allowance for the window is used up — see spec 002.
/// <para>
/// Belongs to a bucket: the subscription's shared pool when <see cref="SeatNumber"/> is null, or
/// one seat. A seat's grant stays with the seat when its holder changes, the same way the seat's
/// own allowance does.
/// </para>
/// <para>
/// <see cref="Remaining"/> only moves through conditional updates on the repository, never by
/// writing the document back, so two recordings drawing on one grant at the same instant cannot
/// both spend its last credit.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionCreditGrant
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string MeterKey { get; set; } = string.Empty;

    /// <summary>Null for the subscription's shared pool; otherwise the seat these credits belong to.</summary>
    public int? SeatNumber { get; set; }

    public CreditGrantSource Source { get; set; }

    /// <summary>How many credits this grant started with. Never changes.</summary>
    public decimal Quantity { get; set; }

    /// <summary>How many are left. Never below zero.</summary>
    public decimal Remaining { get; set; }

    /// <summary>Copied from the meter's terms when granted, so a later edit cannot shorten or extend it.</summary>
    public MeterResetPolicy ExpiryPolicy { get; set; }

    /// <summary>
    /// The end of the usage window this was granted in, for a grant that expires with it. Null for
    /// one that carries forward or never expires.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public CreditGrantStatus Status { get; set; } = CreditGrantStatus.Active;

    public DateTime GrantedAtUtc { get; set; }

    public string GrantedByUserId { get; set; } = string.Empty;

    /// <summary>Why it was granted. Required for a free grant; optional otherwise.</summary>
    public string? Reason { get; set; }

    /// <summary>The purchase that paid for this. Set only when <see cref="Source"/> is Purchased.</summary>
    public string? PurchaseId { get; set; }

    /// <summary>The grant these credits were moved out of. Set only when <see cref="Source"/> is Moved.</summary>
    public string? SourceGrantId { get; set; }

    /// <summary>
    /// The subscription's plan version when this was granted. A carry-forward grant is forfeited
    /// once the plan changes, and this is what lets a reader tell, even before the plan-change
    /// hook has closed it.
    /// </summary>
    public long PlanVersionAtGrant { get; set; }

    /// <summary>When it became exhausted, expired or forfeited.</summary>
    public DateTime? ClosedAtUtc { get; set; }

    public DateTime LastUpdatedAtUtc { get; set; }

    /// <summary>
    /// When the maintenance sweep last confirmed this grant was still live. Null on a grant it has
    /// not looked at yet, which makes it due straight away.
    /// </summary>
    public DateTime? LastCheckedAtUtc { get; set; }

    /// <summary>Raised by every write.</summary>
    public long Version { get; set; }

    /// <summary>
    /// The order grants in one bucket are spent in: whichever would be lost soonest goes first.
    /// </summary>
    /// <remarks>
    /// A dated expiry first, earliest date first. Then carry-forward before never-expiring, both
    /// undated: a carry-forward grant is lost on the next plan change and a never-expiring one is
    /// not, so spending the never-expiring one first could leave the subscriber holding only
    /// credits they are about to forfeit. Oldest first within a tie, and the id last so the order
    /// never depends on how the store happened to return them.
    /// </remarks>
    public static IEnumerable<SubscriptionCreditGrant> InConsumptionOrder(
        IEnumerable<SubscriptionCreditGrant> grants) =>
        grants
            .OrderBy(grant => grant.ExpiresAtUtc ?? DateTime.MaxValue)
            .ThenBy(grant => grant.ExpiryPolicy == MeterResetPolicy.Never ? 1 : 0)
            .ThenBy(grant => grant.GrantedAtUtc)
            .ThenBy(grant => grant.ItemId, StringComparer.Ordinal);

    /// <summary>Whether these credits can still be spent at <paramref name="nowUtc"/>.</summary>
    public bool IsLiveAt(DateTime nowUtc) =>
        Status == CreditGrantStatus.Active &&
        Remaining > 0 &&
        (ExpiresAtUtc is not { } expires || expires > nowUtc);
}
