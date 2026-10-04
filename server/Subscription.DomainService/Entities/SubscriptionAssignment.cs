using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One person occupying one seat on a subscription.
/// </summary>
/// <remarks>
/// Its own record rather than a field on <see cref="SubscriptionDetail"/>, because a subscription
/// can be bought with several seats and a field holds one person. The three ways a seat is filled
/// -- named when the plan is bought, attached afterwards, or handed out one seat at a time -- are
/// then the same operation performed at different moments, rather than three mechanisms that can
/// disagree about who holds what.
/// <para>
/// Nothing here says how many seats were paid for. That is
/// <see cref="SubscriptionQuantityItem.Quantity"/>, which already prices and bills them; this only
/// records who sits in one. A subscription whose seats are all unfilled is fully paid for and
/// grants nobody anything.
/// </para>
/// <para>
/// Organization-wise subscriptions have none of these and never will. They are reached through the
/// organization, exactly as they are today, which is why adding this breaks nothing already
/// running.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionAssignment
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The organization that bought the seat, copied from the subscription.
    /// </summary>
    /// <remarks>
    /// Stored rather than joined, so "what is this person entitled to" is one query against this
    /// collection instead of a lookup per subscription. It cannot drift: a subscription never
    /// changes organization.
    /// </remarks>
    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>The person holding the seat.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Which of the subscription's seats this is — 1 to however many were bought.
    /// </summary>
    /// <remarks>
    /// The seat is what holds an allowance, not the person in it. Without a stable identity for
    /// the seat, releasing somebody and assigning somebody else would be a new record and so a
    /// fresh allowance, and an organization could mint usage without limit by cycling people
    /// through one place. Whoever takes a seat inherits what is left of that seat's window.
    /// <para>
    /// Numbered rather than given an opaque id so an administrator can be shown the same thing the
    /// billing does: five seats bought, three of them occupied.
    /// </para>
    /// </remarks>
    public int SeatNumber { get; set; }

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the seat was given up, or null while it is still held.
    /// </summary>
    /// <remarks>
    /// Released rather than deleted, so a period's usage can still be explained after the person
    /// who spent it has gone. The meter counts against the subscription and is not reset when a
    /// seat changes hands, so whoever comes next inherits what is left of the window -- and this
    /// is the only record of why the balance was already part spent.
    /// </remarks>
    public DateTime? ReleasedAtUtc { get; set; }

    /// <summary>Who performed the assignment, for the audit trail. Null where no person acted.</summary>
    public string? AssignedByUserId { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
