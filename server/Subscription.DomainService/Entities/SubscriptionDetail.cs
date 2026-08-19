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
