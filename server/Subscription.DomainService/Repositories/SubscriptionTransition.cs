using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// One status change and everything that must be written atomically with it.
/// </summary>
/// <remarks>
/// Grouped into a single value rather than passed as a long parameter list, so a caller cannot
/// set a status without saying what it expected the previous one to be. The event travels here
/// too because appending it in the same update is what makes publication survive a crash: Mongo
/// and the bus share no transaction, so a separate write would lose events precisely when
/// something went wrong.
/// </remarks>
public sealed record SubscriptionTransition(
    SubscriptionStatus ExpectedStatus,
    SubscriptionStatus NewStatus)
{
    public DateTime? ActivatedAtUtc { get; init; }

    public DateTime? CanceledAtUtc { get; init; }

    public DateTime? EndedAtUtc { get; init; }

    public bool? CancelAtPeriodEnd { get; init; }

    /// <summary>
    /// Whether the cancellation this transition schedules may later be escalated immediately.
    /// Written alongside <see cref="CancelAtPeriodEnd"/> so the two can never disagree about
    /// which cancellation they describe.
    /// </summary>
    public bool? CanCancelImmediately { get; init; }

    public string? CancellationReason { get; init; }

    public string? InitialPaymentDetailId { get; init; }

    public string? LastRenewalPaymentDetailId { get; init; }

    public DateTime? CurrentPeriodStartUtc { get; init; }

    public DateTime? CurrentPeriodEndUtc { get; init; }

    public DateTime? NextFeeBillingAtUtc { get; init; }

    /// <summary>Explicitly clears the next billing instant, which a null value cannot express.</summary>
    public bool ClearNextFeeBillingAt { get; init; }

    public DateTime? PastDueSinceUtc { get; init; }

    /// <summary>Explicitly clears the dunning start, which a null value cannot express.</summary>
    public bool ClearPastDueSinceAt { get; init; }

    public int? DunningAttemptCount { get; init; }

    public int? DiscountPeriodsApplied { get; init; }

    /// <summary>
    /// What the first paid period cost, written when that period is actually created.
    /// </summary>
    /// <remarks>
    /// Set by a card-free trial converting to paid, which is the one path where the opening charge
    /// is not known at signup: what it costs depends on when the trial ends. Every other path
    /// freezes these at checkout creation and never writes them again.
    /// </remarks>
    public long? InitialChargeAmountMinor { get; init; }

    public bool? InitialChargeProrated { get; init; }

    public bool? InitialChargeDiscountApplied { get; init; }

    public int? ProrationDays { get; init; }

    public int? ProrationTotalDays { get; init; }

    /// <summary>
    /// Whether to discard the pending annual period, because this transition is the one opening it.
    /// </summary>
    /// <remarks>
    /// Written with the period it opens, so moving into the year and forgetting that it was pending
    /// cannot come apart. A boundary that opened the year and then failed to clear this would find
    /// it again on the next sweep and charge for it twice.
    /// </remarks>
    public bool ClearPendingAnnualPeriod { get; init; }

    /// <summary>
    /// The year to start holding, when this transition is the one that priced it.
    /// </summary>
    /// <remarks>
    /// Written by a card-free trial converting to paid, which is the only path where the year is
    /// not knowable at signup — what it covers depends on when the trial ends.
    /// </remarks>
    public PendingAnnualPeriod? PendingAnnualPeriod { get; init; }

    /// <summary>
    /// Marks the pending year as paid, because this transition recorded the payment that covered
    /// it. Set by activation on a price that collects the year at checkout.
    /// </summary>
    public bool MarkPendingAnnualPeriodPrepaid { get; init; }

    public long? CreditBalanceMinor { get; init; }

    public DateTime? CurrentUsagePeriodStartUtc { get; init; }

    public DateTime? CurrentUsagePeriodEndUtc { get; init; }

    public DateTime? NextUsageBillingAtUtc { get; init; }

    /// <summary>Explicitly clears the next usage-rating instant, which a null value cannot express.</summary>
    public bool ClearNextUsageBillingAt { get; init; }

    /// <summary>
    /// The purchased quantities as of this transition, when a renewal is carrying out a decrease
    /// that was scheduled for the end of the period now closing.
    /// </summary>
    public List<SubscriptionQuantityItem>? QuantityItems { get; init; }

    /// <summary>
    /// Whether to discard the scheduled quantity change. Set with
    /// <see cref="QuantityItems"/> so applying a decrease and forgetting it are one write: a
    /// renewal that applied the quantity and then failed to clear the schedule would apply it
    /// again next period.
    /// </summary>
    public bool ClearPendingQuantityChange { get; init; }

    /// <summary>
    /// Whether this transition must not happen while a quantity increase is mid-settlement.
    /// </summary>
    /// <remarks>
    /// Set by renewals only. The in-memory check in the renewal sweep closes the ordinary case;
    /// this closes the gap between reading the subscription and writing the transition, where a
    /// reservation can be taken by a request arriving in between.
    /// <para>
    /// Deliberately opt-in rather than the default for every transition. Activation, cancellation
    /// and usage rating share this write, and a reservation whose charge the provider never answers
    /// for can only be cleared by a person — a blanket lock would let one stall a subscription's
    /// whole lifecycle rather than one period of its billing.
    /// </para>
    /// </remarks>
    public bool RequireNoSettlementReservation { get; init; }

    public SubscriptionOutboxEvent? Event { get; init; }
}
