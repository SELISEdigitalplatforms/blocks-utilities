using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// An organization's subscription to a plan. The aggregate everything else hangs off.
/// </summary>
/// <remarks>
/// Named <c>SubscriptionDetail</c> rather than <c>Subscription</c> because a type sharing its
/// name with the root namespace is ambiguous to the compiler — the same reason payments have a
/// <c>PaymentDetail</c> and no <c>Payment</c>.
/// <para>
/// The organization here is the <em>subscriber</em> — the customer who pays — not a merchant.
/// The tenant holds the merchant configuration; its organizations are its customers.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionDetail
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>The subscribing organization. Always present: entitlement without one is meaningless.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    public string BillingAccountId { get; set; } = string.Empty;

    public SubscriptionStatus Status { get; set; } =
        SubscriptionStatus.Incomplete;

    /// <summary>
    /// Fixed for the life of the subscription. Refunds must return in the currency they
    /// arrived in, and invoice history that spans currencies cannot be totalled.
    /// </summary>
    public string CurrencyCode { get; set; } = string.Empty;

    public PlanSnapshot Plan { get; set; } = new();

    public PriceSnapshot Price { get; set; } = new();

    public List<SubscriptionQuantityItem> QuantityItems { get; set; } = [];

    /// <summary>When the subscription fee falls due.</summary>
    public BillingSchedule FeeSchedule { get; set; } = new();

    /// <summary>
    /// When metered usage is counted and settled. Independent of the fee schedule on purpose:
    /// an annual plan still meters monthly, since waiting a year to bill usage is a year of
    /// unsecured credit.
    /// </summary>
    public BillingSchedule UsageSchedule { get; set; } = new();

    public TrialTerms? Trial { get; set; }

    public DiscountTerms? Discount { get; set; }

    /// <summary>
    /// How many periods <see cref="Discount"/> has already reduced the charge for. Set to 1 at
    /// creation when a discount was applied to the first charge; the renewal service increments
    /// it further whenever it applies the discount again, so <see cref="DiscountTerms.DurationPeriods"/>
    /// can be enforced without re-deriving history from past charges.
    /// </summary>
    public int DiscountPeriodsApplied { get; set; }

    /// <summary>
    /// What the first charge was fixed at when checkout was created, if it was ever calculated.
    /// </summary>
    /// <remarks>
    /// Frozen rather than derived, because the alternative is a price that moves while the
    /// customer is looking at it. A calendar-aligned first period is a fraction of a month, and
    /// recalculating it when a customer returns to a checkout the next morning would quote them
    /// one day less than the page they left open says — the same charge has to survive a delayed
    /// payment, a resumed checkout and a recovery sweep.
    /// <para>
    /// Null on subscriptions created before this existed, and on any path that never priced a
    /// first period. Kept after activation for tracing: "why was this customer charged 32.74"
    /// has no other answer once the period it covered has closed.
    /// </para>
    /// </remarks>
    public long? InitialChargeAmountMinor { get; set; }

    /// <summary>Whether that first charge covered part of a month rather than all of one.</summary>
    public bool InitialChargeProrated { get; set; }

    /// <summary>Calendar dates the first period covered — the 7 of "7/31". Null when not prorated.</summary>
    public int? ProrationDays { get; set; }

    /// <summary>Dates in the month it was a fraction of — the 31 of "7/31". Null when not prorated.</summary>
    public int? ProrationTotalDays { get; set; }

    /// <summary>
    /// Banked value from a downgrade's unused time, in this subscription's own currency.
    /// Consumed automatically by future renewals before anything is charged — never paid out.
    /// </summary>
    public long CreditBalanceMinor { get; set; }

    /// <summary>
    /// Derived from the subscription id and stored, so a charge can be found again after a
    /// crash between raising it and recording the link to it.
    /// </summary>
    public string OrderId { get; set; } = string.Empty;

    public string? InitialPaymentDetailId { get; set; }

    /// <summary>The most recent renewal's payment, for support traceability.</summary>
    public string? LastRenewalPaymentDetailId { get; set; }

    /// <summary>When the current dunning cycle started. Null outside <see cref="SubscriptionStatus.PastDue"/>.</summary>
    public DateTime? PastDueSinceUtc { get; set; }

    /// <summary>Renewal attempts made in the current dunning cycle. Resets to 0 on every successful charge.</summary>
    public int DunningAttemptCount { get; set; }

    public DateTime CurrentPeriodStartUtc { get; set; }

    public DateTime CurrentPeriodEndUtc { get; set; }

    public DateTime? NextFeeBillingAtUtc { get; set; }

    public DateTime CurrentUsagePeriodStartUtc { get; set; }

    public DateTime CurrentUsagePeriodEndUtc { get; set; }

    public DateTime? NextUsageBillingAtUtc { get; set; }

    /// <summary>
    /// Usage windows atomically detached by plan changes and still awaiting rating under their
    /// original plan terms. This prevents both lost overage and a free allowance reset.
    /// </summary>
    public List<PendingUsagePeriod> PendingUsagePeriods { get; set; } = [];

    /// <summary>
    /// A reduction in purchased quantity waiting for the paid period to end, if one is scheduled.
    /// </summary>
    /// <remarks>
    /// Singular, and replaced rather than queued: two decreases in one period is a customer
    /// changing their mind, not two instructions to carry out.
    /// </remarks>
    public PendingQuantityChange? PendingQuantityChange { get; set; }

    /// <summary>
    /// An increase reserved but not yet settled, if one is in flight.
    /// </summary>
    /// <remarks>
    /// Present only between the reservation and its charge settling — normally for the length of
    /// one card authorization. A claim still here minutes later is a caller that died mid-flight,
    /// which the reconciliation sweep resolves by asking the payment module what became of the
    /// charge.
    /// </remarks>
    public SettlementReservation? SettlementReservation { get; set; }

    public DateTime? ActivatedAtUtc { get; set; }

    /// <summary>Whether cancellation has been requested but not yet taken effect.</summary>
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>When cancellation was asked for — separate from when it takes effect.</summary>
    public DateTime? CanceledAtUtc { get; set; }

    /// <summary>When entitlement actually stopped.</summary>
    public DateTime? EndedAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    public List<SubscriptionOutboxEvent> OutboxEvents { get; set; } = [];

    /// <summary>Starts at 1: a zero version cannot be told apart from an absent field.</summary>
    public int Version { get; set; } = 1;

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
