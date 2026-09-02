using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// What one usage window may spend, and what the window before it leaves behind.
/// </summary>
public sealed class MeterAllowanceTests
{
    private static readonly DateTime Anchor = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_unused_remainder_rolls_into_the_next_window()
    {
        // Spent 400 of 1,000 with a cap well clear of the remainder, so all 600 rolls.
        MeterAllowance
            .CarriedIn(Subscription(), Meter(cap: 5_000), Previous(), Counter(limit: 1_000, balance: 400))
            .Should().Be(600);
    }

    [Fact]
    public void The_cap_bounds_what_one_window_may_receive()
    {
        // Bounds the amount carried, not the total: the plan's own quantity is still available
        // on top of this.
        MeterAllowance
            .CarriedIn(Subscription(), Meter(cap: 250), Previous(), Counter(limit: 1_000, balance: 0))
            .Should().Be(250);
    }

    [Fact]
    public void A_window_that_went_into_overage_carries_nothing_rather_than_a_debt()
    {
        // The overage was already invoiced. Carrying the negative would charge for it twice.
        MeterAllowance
            .CarriedIn(Subscription(), Meter(cap: 400), Previous(), Counter(limit: 1_000, balance: 1_600))
            .Should().Be(0);
    }

    [Fact]
    public void A_window_that_recorded_nothing_carries_the_plans_whole_quantity()
    {
        // No usage in a window means no counter document. Treating that as zero carried would
        // break the roll on exactly the idle month a customer expects to have banked.
        MeterAllowance
            .CarriedIn(Subscription(), Meter(cap: 5_000), Previous(), previousCounter: null)
            .Should().Be(1_000);
    }

    [Fact]
    public void A_roll_compounds_because_it_measures_the_previous_windows_own_allowance()
    {
        // The previous window opened with 1,000 + 600 carried and spent 200, so 1,400 is unused.
        MeterAllowance
            .CarriedIn(Subscription(), Meter(cap: 5_000), Previous(), Counter(limit: 1_600, balance: 200))
            .Should().Be(1_400);
    }

    [Fact]
    public void Any_other_reset_policy_carries_nothing()
    {
        foreach (var policy in new[] { MeterResetPolicy.Periodic, MeterResetPolicy.Never })
        {
            MeterAllowance
                .CarriedIn(Subscription(), Meter(cap: null, policy: policy), Previous(),
                    Counter(limit: 1_000, balance: 0))
                .Should().Be(0, "only a carry-forward meter rolls its remainder");
        }
    }

    [Fact]
    public void Nothing_carries_into_a_trial()
    {
        // The grant is meant to be the whole trial allowance, not a float on top of one.
        var trialing = Subscription(status: SubscriptionStatus.Trialing);
        trialing.Trial = new TrialTerms { EndsAtUtc = Anchor.AddDays(14) };

        MeterAllowance
            .CarriedIn(trialing, Meter(cap: 400), Previous(), Counter(limit: 1_000, balance: 0))
            .Should().Be(0);
    }

    [Fact]
    public void Nothing_carries_out_of_a_window_that_overlapped_the_trial()
    {
        var subscription = Subscription();
        subscription.Trial = new TrialTerms { EndsAtUtc = Anchor.AddMonths(2) };

        MeterAllowance
            .CarriedIn(subscription, Meter(cap: 400), Previous(), Counter(limit: 1_000, balance: 0))
            .Should().Be(0, "a trial grant must not become a way to bank allowance");
    }

    /// <summary>
    /// A plan change re-anchors the usage schedule at the change instant, so the window before it
    /// starts earlier than the anchor. Carrying across the change would make repeated plan changes
    /// a way to collect fresh allowance.
    /// </summary>
    [Fact]
    public void Nothing_carries_across_a_plan_change_or_out_of_the_first_window()
    {
        var subscription = Subscription();
        subscription.UsageSchedule.AnchorInstantUtc = Anchor.AddMonths(3);

        MeterAllowance
            .CarriedIn(subscription, Meter(cap: 400), Previous(), Counter(limit: 1_000, balance: 0))
            .Should().Be(0);
    }

    [Fact]
    public void The_windows_frozen_snapshot_beats_a_recomputed_figure()
    {
        // Whatever happens to the previous window afterwards, this window keeps the number it
        // opened with.
        MeterAllowance.Effective(Counter(limit: 1_600, balance: 0), computed: 1_000)
            .Should().Be(1_600);

        MeterAllowance.Effective(counter: null, computed: 1_000).Should().Be(1_000);
    }

    [Fact]
    public void A_trial_grant_replaces_the_plans_quantity_rather_than_adding_to_it()
    {
        var trialing = Subscription(status: SubscriptionStatus.Trialing);
        trialing.Trial = new TrialTerms
        {
            EndsAtUtc = Anchor.AddDays(14),
            Grants = [new TrialMeterGrant { MeterKey = "tokens", IncludedQuantity = 50 }]
        };

        MeterAllowance.Base(trialing, Meter(cap: 400)).Should().Be(50);
        MeterAllowance.Base(Subscription(), Meter(cap: 400)).Should().Be(1_000);
    }

    private static SubscriptionDetail Subscription(
        SubscriptionStatus status = SubscriptionStatus.Active) => new()
    {
        ItemId = "sub-1",
        TenantId = "tenant-1",
        Status = status,
        CreatedAtUtc = Anchor,
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = Anchor
        }
    };

    // ------------------------------------------------------------------ fractional quantities

    /// <summary>
    /// A fractional remainder carries forward exactly, with no residue and no rounding.
    /// </summary>
    /// <remarks>
    /// The subtraction that computes what went unused is the one place a binary residue would
    /// become a permanent discrepancy: it would be frozen into the next window's allowance
    /// snapshot and then measured against for the whole period.
    /// </remarks>
    [Fact]
    public void A_fractional_remainder_carries_forward_exactly()
    {
        var carried = MeterAllowance.CarriedIn(
            Subscription(),
            FractionalMeter(cap: 500m),
            Previous(),
            Counter(limit: 1_000m, balance: 999.75m));

        carried.Should().Be(0.25m);
    }

    /// <summary>
    /// The cap bounds a fractional carry the same way it bounds a whole one — and it is read from
    /// the meter, which is the propagation this change also had to repair.
    /// </summary>
    [Fact]
    public void A_fractional_carry_is_still_bounded_by_the_cap()
    {
        var carried = MeterAllowance.CarriedIn(
            Subscription(),
            FractionalMeter(cap: 0.5m),
            Previous(),
            Counter(limit: 1_000m, balance: 100.25m));

        carried.Should().Be(0.5m);
    }

    /// <summary>
    /// Thirds carried across three windows do not accumulate a drift.
    /// </summary>
    /// <remarks>
    /// Each window's carry is computed from the previous window's own snapshot, so an inexact
    /// representation would compound rather than cancel.
    /// </remarks>
    [Fact]
    public void Repeated_fractional_carries_do_not_drift()
    {
        var meter = FractionalMeter(cap: 1_000m);
        var used = 0.333333m;
        var carried = 0m;

        for (var window = 0; window < 3; window++)
        {
            carried = MeterAllowance.CarriedIn(
                Subscription(),
                meter,
                Previous(),
                Counter(limit: 1_000m, balance: used));
        }

        carried.Should().Be(1_000m - 0.333333m);
    }

    private static PlanMeter FractionalMeter(decimal? cap) => new()
    {
        MeterKey = "tokens",
        QuantityScale = 6,
        IncludedQuantity = 1_000,
        ResetPolicy = MeterResetPolicy.CarryForward,
        CarryForwardCap = cap
    };

    private static PlanMeter Meter(
        decimal? cap,
        MeterResetPolicy policy = MeterResetPolicy.CarryForward) => new()
    {
        MeterKey = "tokens",
        IncludedQuantity = 1_000,
        ResetPolicy = policy,
        CarryForwardCap = cap
    };

    /// <summary>The window before the one being opened — the third month of the subscription.</summary>
    private static BillingPeriod Previous() =>
        new(1, Anchor.AddMonths(1), Anchor.AddMonths(2), "M20260201T000000Z");

    private static SubscriptionUsageCounter Counter(decimal limit, decimal balance) => new()
    {
        MeterKey = "tokens",
        LimitSnapshot = limit,
        Balance = balance
    };
}
