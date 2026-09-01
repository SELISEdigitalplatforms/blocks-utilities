using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A change to a subscription that has been reserved on it and is waiting for its charge to settle.
/// </summary>
/// <remarks>
/// Written <em>before</em> the card is charged, which is the whole point. Charging first and then
/// writing leaves the money moved and the change unapplied whenever the write loses a
/// compare-and-set, with no way back: the charge was keyed on the version the write just found
/// stale, so a retry raises a second charge rather than finding the first.
/// <para>
/// Reserving first inverts that. The one versioned write happens while nothing has been spent, the
/// charge is keyed on <see cref="ReservationId"/> — which no concurrent change can move — and the
/// promotion that applies the change is addressed by the same id rather than by a version. A
/// declined card releases the reservation and the subscription stands exactly as it did.
/// </para>
/// <para>
/// One reservation at a time, whatever its kind. A second money-moving change while one is in
/// flight is refused rather than queued: the second is being quoted against a subscription the
/// first has already half-changed.
/// </para>
/// <para>
/// Renewals deliberately do not take one. Their charge is keyed on the period and the attempt
/// number, neither of which a lost write can move, so a retried renewal already finds the charge it
/// raised instead of raising another. A reservation would add a lock without removing a risk.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SettlementReservation
{
    /// <summary>
    /// What the charge is keyed on and what the promotion is addressed by. Stable for the life of
    /// the reservation, so every retry of either finds the one attempt rather than starting another.
    /// </summary>
    public string ReservationId { get; set; } = string.Empty;

    public SettlementReservationKind Kind { get; set; }

    /// <summary>What this reservation is charging for.</summary>
    public long ChargeAmountMinor { get; set; }

    public DateTime ReservedAtUtc { get; set; }

    public string? RequestedByUserId { get; set; }

    /// <summary>Carried so a recovering sweep logs under the request that opened the reservation.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// The version the reservation was taken against, for the audit trail. Never used to address
    /// the promotion — a concurrent change moving the version must not strand a paid-for change.
    /// </summary>
    public int ReservedAtVersion { get; set; }

    /// <summary>
    /// Where the charge was sent: the account, the provider, the customer and the card, exactly as
    /// the attempt used them.
    /// </summary>
    /// <remarks>
    /// Snapshotted because a replay has to repeat <em>that</em> attempt. Read from the billing
    /// account as it stands now instead, a replay could go to a different card, a different provider
    /// customer, or nowhere at all — and today's account saying there is no card is not evidence
    /// about what the provider did an hour ago. A card removed after the money moved would otherwise
    /// look exactly like a charge that never happened.
    /// </remarks>
    public string BillingAccountId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string? ProviderOrganizationId { get; set; }

    public string? ProviderCustomerId { get; set; }

    public string StoredPaymentMethodId { get; set; } = string.Empty;

    /// <summary>
    /// How the charge was arrived at, snapshotted with everything else about the attempt.
    /// </summary>
    /// <remarks>
    /// Held here for the same reason the card and the target terms are: a replay has to record
    /// <em>that</em> attempt. Recomputing the proration at settlement time would price it against a
    /// different instant, and possibly an edited catalogue, producing an explanation of a charge that
    /// nobody was quoted.
    /// </remarks>
    public SubscriptionSettlementBreakdown? Settlement { get; set; }

    /// <summary>What to write when a quantity increase settles. Null for any other kind.</summary>
    public ReservedQuantityChange? QuantityChange { get; set; }

    /// <summary>What to write when a plan change settles. Null for any other kind.</summary>
    public ReservedPlanChange? PlanChange { get; set; }
}

/// <summary>The quantities a settled increase grants.</summary>
[BsonIgnoreExtraElements]
public sealed class ReservedQuantityChange
{
    public List<SubscriptionQuantityItem> RequestedQuantities { get; set; } = [];

    public long NewCreditBalanceMinor { get; set; }
}

/// <summary>
/// The terms a settled plan change moves onto.
/// </summary>
/// <remarks>
/// Held in full rather than recalculated at promotion time. The catalogue may have been edited, and
/// the schedule was built from the instant the change was asked for — recomputing either would
/// deliver something the customer was never quoted and has already paid for.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class ReservedPlanChange
{
    public PlanSnapshot Plan { get; set; } = new();

    public PriceSnapshot Price { get; set; } = new();

    public List<SubscriptionQuantityItem> QuantityItems { get; set; } = [];

    public SubscriptionPlanSchedule Schedule { get; set; } = null!;

    public PendingUsagePeriod OutgoingUsagePeriod { get; set; } = new();

    public long NewCreditBalanceMinor { get; set; }

    /// <summary>
    /// The prepaid annual period to install in place of the one this change settles alongside its
    /// opening stub. Null for every ordinary plan change, which touches no annual period at all.
    /// </summary>
    /// <remarks>
    /// Snapshotted at reservation time rather than recomputed on promotion, for the same reason as
    /// every other field here: a crash between charge and promotion must apply exactly what was
    /// charged for, not whatever the catalogue says now.
    /// </remarks>
    public PendingAnnualPeriod? ReplacementPendingAnnualPeriod { get; set; }
}
