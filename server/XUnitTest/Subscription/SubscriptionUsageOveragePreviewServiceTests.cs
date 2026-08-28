using FluentAssertions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Previewing the cost of additional metered usage — advisory, and never writing anything.
/// </summary>
public sealed class SubscriptionUsageOveragePreviewServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = NewSubscription();

    public SubscriptionUsageOveragePreviewServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _usage
            .Setup(repository => repository.SummariseLedgerAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0L, 0L));

        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = 170, LimitSnapshot = 150 });

        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task Current_usage_plus_additional_produces_the_projected_totals()
    {
        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CurrentUsage.Should().Be(170);
        result.Value.IncludedQuantity.Should().Be(150);
        result.Value.CurrentOverage.Should().Be(20);
        result.Value.ProjectedUsage.Should().Be(270);
        result.Value.ProjectedOverage.Should().Be(120);
    }

    [Fact]
    public async Task The_documented_example_figures_match_exactly()
    {
        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        var response = result.Value!;
        response.CurrentCharge.GrossMinor.Should().Be(2_000);
        response.CurrentCharge.TaxMinor.Should().Be(154);
        response.CurrentCharge.TotalMinor.Should().Be(2_154);
        response.AdditionalCharge.GrossMinor.Should().Be(10_000);
        response.AdditionalCharge.TaxMinor.Should().Be(770);
        response.AdditionalCharge.TotalMinor.Should().Be(10_770);
        response.ProjectedPeriodCharge.GrossMinor.Should().Be(12_000);
        response.ProjectedPeriodCharge.TaxMinor.Should().Be(924);
        response.ProjectedPeriodCharge.TotalMinor.Should().Be(12_924);
    }

    [Fact]
    public async Task The_additional_total_is_the_projected_total_minus_the_current_total()
    {
        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        var response = result.Value!;
        response.AdditionalCharge.TotalMinor.Should().Be(
            response.ProjectedPeriodCharge.TotalMinor - response.CurrentCharge.TotalMinor);
        response.AdditionalCharge.TaxMinor.Should().Be(
            response.ProjectedPeriodCharge.TaxMinor - response.CurrentCharge.TaxMinor);
        response.AdditionalCharge.NetMinor.Should().Be(
            response.ProjectedPeriodCharge.NetMinor - response.CurrentCharge.NetMinor);
    }

    [Fact]
    public async Task Additional_usage_crossing_a_tier_boundary_reports_the_exact_bands()
    {
        _subscription = NewSubscription(tiers:
        [
            new MeterTier { UpToQuantity = 50, UnitAmountMinor = 10 },
            new MeterTier { UpToQuantity = null, UnitAmountMinor = 5 }
        ]);
        // 30 current overage, 40 additional — spans the remaining 20 units of the first tier and
        // 20 units of the second.
        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = 180, LimitSnapshot = 150 });

        var result = await Service().PreviewAsync(NewRequest(40), "corr-1", CancellationToken.None);

        var breakdown = result.Value!.AdditionalTierBreakdown;
        breakdown.Should().HaveCount(2);
        breakdown[0].FromOverageQuantity.Should().Be(31);
        breakdown[0].ToOverageQuantity.Should().Be(50);
        breakdown[0].Units.Should().Be(20);
        breakdown[0].UnitAmountMinor.Should().Be(10);
        breakdown[0].AmountMinor.Should().Be(200);
        breakdown[1].FromOverageQuantity.Should().Be(51);
        breakdown[1].ToOverageQuantity.Should().Be(70);
        breakdown[1].Units.Should().Be(20);
        breakdown[1].UnitAmountMinor.Should().Be(5);
        breakdown[1].AmountMinor.Should().Be(100);
    }

    [Theory]
    [InlineData(AutomaticDiscountCombination.BestDiscount)]
    [InlineData(AutomaticDiscountCombination.Additive)]
    public async Task Automatic_discounts_apply_under_either_combination_policy(
        AutomaticDiscountCombination combination)
    {
        _subscription.Price.AutomaticDiscountBasisPoints = 800;
        _subscription.Price.QuantityDiscountCombination = combination;

        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        result.Value!.CurrentCharge.AutomaticDiscountMinor.Should().Be(160);
        result.Value.Discount.AutomaticBasisPoints.Should().Be(800);
    }

    [Fact]
    public async Task A_promotional_discount_never_reaches_the_preview_and_is_reported_as_such()
    {
        _subscription.Discount = new DiscountTerms
        {
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 5_000
        };

        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        result.Value!.Discount.PromotionalCodeApplied.Should().BeFalse();
        // 20% off would have shown here had the promotion reached usage; it must not.
        result.Value.CurrentCharge.GrossMinor.Should().Be(2_000);
    }

    [Fact]
    public async Task Inclusive_tax_matches_the_period_end_calculation_exactly()
    {
        _subscription.Price.TaxRateBasisPoints = 1_000;
        _subscription.Price.TaxMode = TaxMode.Inclusive;

        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        result.Value!.CurrentCharge.TotalMinor.Should().Be(2_000);
        result.Value.CurrentCharge.TaxMinor.Should().Be(182);
        result.Value.CurrentCharge.NetMinor.Should().Be(1_818);
        result.Value.Tax.Mode.Should().Be(nameof(TaxMode.Inclusive));
    }

    [Fact]
    public async Task A_trial_grant_replaces_the_included_quantity()
    {
        _subscription.Status = SubscriptionStatus.Trialing;
        _subscription.Trial = new TrialTerms
        {
            StartsAtUtc = _time.GetUtcNow().UtcDateTime.AddDays(-1),
            EndsAtUtc = _time.GetUtcNow().UtcDateTime.AddDays(13),
            Grants = [new TrialMeterGrant { MeterKey = "screening", IncludedQuantity = 25 }]
        };
        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionUsageCounter?)null);

        var result = await Service().PreviewAsync(NewRequest(10), "corr-1", CancellationToken.None);

        result.Value!.IncludedQuantity.Should().Be(25,
            "a trial grant replaces the plan's own included quantity");
    }

    [Fact]
    public async Task A_missing_currency_rate_table_returns_a_named_error_rather_than_zero()
    {
        _subscription.Plan.Meters[0].RateTables[0].CurrencyCode = "EUR";

        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_meter_rate_unavailable");
        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
    }

    [Fact]
    public async Task Overage_not_allowed_is_a_named_conflict_not_a_zero_charge()
    {
        _subscription.Plan.Meters[0].OverageAllowed = false;

        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_meter_overage_not_allowed");
        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
    }

    [Fact]
    public async Task Ledger_usage_wins_when_a_crash_left_the_counter_projection_behind()
    {
        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = 100, LimitSnapshot = 150 });
        _usage
            .Setup(repository => repository.SummariseLedgerAsync(
                TenantId, "sub-1", "screening", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((170L, 3L));

        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        result.Value!.CurrentUsage.Should().Be(170,
            "the ledger is authoritative whenever it has records at all");
    }

    [Fact]
    public async Task No_meter_is_not_found()
    {
        var request = NewRequest(100);
        request.MeterKey = "not-a-meter";

        var result = await Service().PreviewAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_meter_not_found");
        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
    }

    [Fact]
    public async Task No_subscription_is_not_found()
    {
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_not_found");
    }

    [Fact]
    public async Task An_additional_quantity_that_would_overflow_the_projection_is_a_named_failure()
    {
        // A valid positive AdditionalQuantity added to a balance already near long.MaxValue would
        // otherwise wrap into a negative projected usage — checked arithmetic must catch this
        // rather than let it silently produce a nonsensical negative charge.
        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = long.MaxValue - 10, LimitSnapshot = 150 });

        var result = await Service().PreviewAsync(NewRequest(20), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("subscription_usage_preview_invalid");
    }

    /// <summary>
    /// Hardening beyond the currentUsage+additionalQuantity check above: a technically valid unit
    /// rate and a technically valid (if unusual) overage quantity can still multiply past
    /// <c>long.MaxValue</c> once the tier total's <c>Int128</c> is narrowed back down to
    /// <c>long</c>. See SubscriptionUsageRater.WalkTierRange's own remarks.
    /// </summary>
    [Fact]
    public async Task A_tier_total_that_would_overflow_a_long_is_a_named_failure_not_a_wrapped_charge()
    {
        _subscription = NewSubscription(tiers:
        [
            new MeterTier { UpToQuantity = null, UnitAmountMinor = 5_000_000_000_000_000_000 }
        ]);
        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = 153, LimitSnapshot = 150 });

        var result = await Service().PreviewAsync(NewRequest(1), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse(
            "3 overage units at 5 quintillion each is ~15 quintillion — past long.MaxValue " +
            "(~9.2 quintillion) — and must be refused rather than silently wrapped negative");
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("subscription_usage_preview_invalid");
    }

    /// <summary>
    /// A distinct failure point from the tier-walk overflow above: a gross overage total that
    /// comfortably fits a <c>long</c> on its own — the tier walk never overflows — can still
    /// overflow once this subscription's own 7.7% exclusive tax (see <c>NewSubscription</c>) is
    /// added on top inside <c>SubscriptionAmountCalculator.TaxBreakdownFor</c>. Must be refused
    /// the same way, not bubble up as an unhandled 500.
    /// </summary>
    [Fact]
    public async Task A_gross_total_that_only_overflows_after_tax_is_a_named_failure()
    {
        _subscription = NewSubscription(tiers:
        [
            new MeterTier { UpToQuantity = null, UnitAmountMinor = 9_000_000_000_000_000_000 }
        ]);
        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = 151, LimitSnapshot = 150 });

        var result = await Service().PreviewAsync(NewRequest(1), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse(
            "1 overage unit at 9 quintillion is a gross that fits a long, but 7.7% tax on top of " +
            "it does not, and must be refused rather than silently wrapped");
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("subscription_usage_preview_invalid");
    }

    [Fact]
    public async Task A_non_positive_additional_quantity_is_refused()
    {
        var result = await Service().PreviewAsync(NewRequest(0), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("subscription_usage_preview_invalid");
    }

    [Fact]
    public async Task The_response_states_it_neither_writes_nor_charges_anything()
    {
        var result = await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        result.Value!.WritesUsage.Should().BeFalse();
        result.Value.ChargesPayment.Should().BeFalse();
        result.Value.FinalChargeDependsOnActualPeriodEndUsage.Should().BeTrue();
    }

    [Fact]
    public async Task Nothing_is_ever_written_to_the_usage_ledger_or_counter()
    {
        await Service().PreviewAsync(NewRequest(100), "corr-1", CancellationToken.None);

        _usage.Verify(
            repository => repository.TryAppendRecordAsync(
                It.IsAny<SubscriptionUsageRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _usage.Verify(
            repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _usage.Verify(
            repository => repository.TryRepairCounterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _usage.Verify(
            repository => repository.TryMarkThresholdNotifiedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private SubscriptionUsageOveragePreviewService Service() => new(
        _subscriptions.Object,
        _usage.Object,
        new MeterAllowanceResolver(_usage.Object),
        _contextResolver.Object,
        new PreviewUsageOverageRequestValidator(),
        _time);

    private static PreviewUsageOverageRequest NewRequest(long additionalQuantity) => new()
    {
        MeterKey = "screening",
        AdditionalQuantity = additionalQuantity
    };

    private static SubscriptionDetail NewSubscription(List<MeterTier>? tiers = null) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Price = new PriceSnapshot
        {
            CurrencyCode = "CHF",
            TaxRateBasisPoints = 770,
            TaxMode = TaxMode.Exclusive
        },
        CurrentUsagePeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentUsagePeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        },
        Plan = new PlanSnapshot
        {
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "screening",
                    UnitLabel = "screenings",
                    IncludedQuantity = 150,
                    OverageAllowed = true,
                    RateTables =
                    [
                        new MeterRateTable
                        {
                            CurrencyCode = "CHF",
                            Tiers = tiers ?? [new MeterTier { UpToQuantity = null, UnitAmountMinor = 100 }]
                        }
                    ]
                }
            ]
        }
    };
}
