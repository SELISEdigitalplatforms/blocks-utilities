using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The plan's terms as they stood when the subscription was created, copied rather than
/// referenced.
/// </summary>
/// <remarks>
/// Two things follow, and both are wanted. Entitlement becomes a single document read, with no
/// join to a catalogue that may since have moved. And editing a plan stops being retroactive:
/// a subscriber keeps what they were sold until something deliberately migrates them, which is
/// the correct billing semantic as well as the fast one.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PlanSnapshot
{
    public string PlanId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Who the plan was sold to when this subscription bought it.
    /// </summary>
    /// <remarks>
    /// Copied here like everything else in this snapshot, but for a second reason as well: the
    /// signup reservation index needs it. One subscription per organization is the rule for
    /// organization-wise plans only — user-wise ones are held several to an organization — so the
    /// index's partial filter has to name the scope, and a partial filter can only read a field
    /// that is on the document being indexed.
    /// <para>
    /// A subscription written before this existed has no field at all, which a filter on
    /// <see cref="SubscriberScope.Organization"/> does <b>not</b> match — such a document would drop
    /// out of the index and lose the uniqueness every live customer depends on. Every existing
    /// subscription must therefore be backfilled before that filter is switched. See
    /// <c>SubscriptionRepository.BackfillPlanSubscriberScopeAsync</c>.
    /// </para>
    /// </remarks>
    public SubscriberScope SubscriberScope { get; set; } = SubscriberScope.Organization;

    public string DisplayName { get; set; } = string.Empty;

    public string? FeaturesJson { get; set; }

    public BillingInterval UsageInterval { get; set; } = BillingInterval.Month;

    public int UsageIntervalCount { get; set; } = 1;

    public List<PlanEntitlement> Entitlements { get; set; } = [];

    public List<PlanMeter> Meters { get; set; } = [];

    public List<PlanQuantityItem> QuantityItems { get; set; } = [];

    /// <summary>
    /// Whether the plan required a card before activation, as it stood when this was sold.
    /// </summary>
    /// <remarks>
    /// Snapshotted for the same reason everything else here is: what a subscriber was asked for at
    /// signup is a fact about their signup, and a later edit to the catalogue must not be able to
    /// rewrite it. Nothing after activation reads it — by then the card either exists or does not
    /// — but it is what a support question about a stuck checkout is answered from.
    /// </remarks>
    public bool RequirePaymentMethodUpfront { get; set; }

    /// <summary>
    /// Snapshotted with the bands themselves: how they combine with a promotion is part of what
    /// the subscriber was sold, so changing the plan's policy must not reprice them either.
    /// </summary>
    public QuantityDiscountCombinationPolicy QuantityDiscountCombinationPolicy { get; set; } =
        QuantityDiscountCombinationPolicy.BestDiscount;

    /// <summary>The plan version this was taken from, so a migration can tell what is stale.</summary>
    public int PlanVersion { get; set; }

    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}
