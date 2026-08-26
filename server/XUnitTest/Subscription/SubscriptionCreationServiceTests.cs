using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
/// Turning a chosen plan into a subscription, and the things that must be true of the result
/// years later.
/// </summary>
public sealed class SubscriptionCreationServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero));

    private readonly Mock<ISubscriptionBillingProfileGuard> _billingProfile = new();

    private Plan _plan = NewPlan();
    private SubscriptionDetail? _created;

    public SubscriptionCreationServiceTests()
    {
        // Complete unless a test says otherwise: the gate is not what most of these are about, and a
        // default of "incomplete" would make every unrelated test fail for the wrong reason.
        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "professional", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _plan);

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice);

        _accounts
            .Setup(repository => repository.GetOrCreateAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount account, CancellationToken _) => account);

        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionDetail, CancellationToken>(
                (subscription, _) => _created = subscription)
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task The_currency_comes_from_the_price_and_is_fixed()
    {
        var result = await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.CurrencyCode.Should().Be("CHF");
    }

    /// <summary>
    /// Copied onto the subscription like every other term, so a later edit to the catalogue
    /// cannot rewrite what this subscriber was asked for at signup — and so checkout can decide
    /// whether to collect a card from the subscription alone.
    /// </summary>
    [Fact]
    public async Task Whether_a_card_was_demanded_up_front_is_snapshotted_with_the_plan()
    {
        _plan.RequirePaymentMethodUpfront = true;

        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.Plan.RequirePaymentMethodUpfront.Should().BeTrue();
    }

    [Fact]
    public async Task The_organization_comes_from_the_caller_not_the_request()
    {
        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.OrganizationId.Should().Be(OrganizationId,
            "accepting an organization from the body would let anyone subscribe on another " +
            "organization's behalf");
    }

    [Fact]
    public async Task The_plan_snapshot_is_a_copy_not_a_reference()
    {
        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _plan.Entitlements[0].Limit = 1;
        _plan.Meters[0].IncludedQuantity = 1;

        _created!.Plan.Entitlements[0].Limit.Should().Be(500);
        _created.Plan.Meters[0].IncludedQuantity.Should().Be(500,
            "editing the catalogue must not change what an existing subscriber holds");
    }

    [Fact]
    public async Task A_subscription_starts_granting_nothing()
    {
        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.Status.Should().Be(SubscriptionStatus.Incomplete,
            "nobody has paid yet, so walking away must leave nothing granted");
        _created.Version.Should().Be(1);
    }

    [Fact]
    public async Task The_order_id_is_derived_from_the_subscription()
    {
        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.OrderId.Should().Be($"sub:{_created.ItemId}",
            "a crash between raising a charge and recording it leaves this as the only way " +
            "back to the payment");
    }

    /// <summary>
    /// The half of retiring a price that does the work: taking one off the menu means nothing
    /// unless the sale itself refuses it. Existing subscribers are unaffected either way — they
    /// bill from the snapshot copied onto the subscription and never read this row again.
    /// </summary>
    [Fact]
    public async Task A_price_that_has_been_retired_can_no_longer_be_sold()
    {
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var price = NewPrice();
                price.Status = CatalogueStatus.Archived;

                return price;
            });

        var result = await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_price_not_found");
        _created.Should().BeNull();
    }

    [Fact]
    public async Task A_weekly_usage_window_stays_weekly_while_fees_bill_monthly()
    {
        _plan.UsageInterval = BillingInterval.Week;
        _plan.UsageIntervalCount = 1;

        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.FeeSchedule.Interval.Should().Be(BillingInterval.Month);
        _created.UsageSchedule.Interval.Should().Be(BillingInterval.Week);
        _created.NextUsageBillingAtUtc.Should().BeBefore(_created.NextFeeBillingAtUtc!.Value);
    }

    [Fact]
    public async Task An_unknown_discount_code_is_refused_instead_of_ignored()
    {
        var request = NewRequest();
        request.DiscountCode = "missing";

        var result = await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_not_found");
        _created.Should().BeNull();
    }

    [Fact]
    public async Task A_catalogue_discount_is_copied_to_the_subscription()
    {
        var request = NewRequest();
        request.DiscountCode = "launch25";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "launch25", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                Terms = new DiscountTerms { Code = "launch25", Kind = DiscountKind.Percent, PercentBasisPoints = 2500 }
            });

        await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        _created!.Discount.Should().NotBeNull();
        _created.Discount!.PercentBasisPoints.Should().Be(2500);
    }

    [Fact]
    public async Task A_promotion_restricted_to_another_price_is_refused()
    {
        // A code authored for the yearly price, typed against the monthly one. Refused rather than
        // quietly applied, which is the whole reason a price restriction is worth having.
        var request = NewRequest();
        request.DiscountCode = "yearly8";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "yearly8", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ApplicablePriceIds = ["price-yearly"],
                Terms = new DiscountTerms
                {
                    Code = "yearly8",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 800
                }
            });

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_not_applicable");
        _created.Should().BeNull();
    }

    [Fact]
    public async Task A_promotion_restricted_by_plan_and_price_needs_both_to_match()
    {
        // Two restrictions narrow, they do not offer two ways to qualify. The right plan and the
        // wrong price is still the wrong thing to discount.
        var request = NewRequest();
        request.DiscountCode = "both";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "both", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ApplicablePlanCodes = ["professional"],
                ApplicablePriceIds = ["price-yearly"],
                Terms = new DiscountTerms
                {
                    Code = "both",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 800
                }
            });

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_not_applicable");
    }

    [Fact]
    public async Task A_promotion_naming_this_price_is_accepted()
    {
        var request = NewRequest();
        request.DiscountCode = "thisone";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "thisone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ApplicablePlanCodes = ["professional"],
                ApplicablePriceIds = ["price-1"],
                Terms = new DiscountTerms
                {
                    Code = "thisone",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 800
                }
            });

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.Discount!.Code.Should().Be("thisone");
    }

    [Fact]
    public async Task A_discount_stored_before_price_restrictions_existed_stays_unrestricted()
    {
        // Every discount already in a tenant's catalogue is this shape: no price list at all. It has
        // to keep applying to whatever it applied to yesterday.
        var request = NewRequest();
        request.DiscountCode = "legacy";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "legacy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                Terms = new DiscountTerms
                {
                    Code = "legacy",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 1_000
                }
            });

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_promotions_restrictions_are_snapshotted_with_its_terms()
    {
        // Copied so a later plan change can ask the same question the redemption did. Without this the
        // restriction is enforced once and then forgotten, which is how a monthly-only code ends up
        // discounting an annual price.
        var request = NewRequest();
        request.DiscountCode = "thisone";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "thisone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ApplicablePlanCodes = ["professional"],
                ApplicablePriceIds = ["price-1"],
                Terms = new DiscountTerms
                {
                    Code = "thisone",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 800
                }
            });

        await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        _created!.Discount!.ApplicablePlanCodes.Should().Equal("professional");
        _created.Discount.ApplicablePriceIds.Should().Equal("price-1");
    }

    [Fact]
    public async Task The_prices_automatic_discount_is_snapshotted_onto_the_subscription()
    {
        // The copy is what makes a catalogue edit safe. Without it, clearing the discount tomorrow
        // would raise the price of every subscription already sold on it.
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var price = NewPrice();
                price.AutomaticDiscountBasisPoints = 800;
                price.QuantityDiscountCombination = AutomaticDiscountCombination.Additive;

                return price;
            });

        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.Price.AutomaticDiscountBasisPoints.Should().Be(800);
        _created.Price.QuantityDiscountCombination
            .Should().Be(AutomaticDiscountCombination.Additive);
    }

    [Fact]
    public async Task A_missing_quantity_takes_the_plans_default()
    {
        var request = NewRequest();
        request.Quantities.Clear();

        await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        _created!.QuantityItems.Should().ContainSingle()
            .Which.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task A_quantity_beyond_the_plans_maximum_is_refused()
    {
        _plan.QuantityItems[0].MaxQuantity = 10;

        var request = NewRequest();
        request.Quantities[0].Quantity = 11;

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _subscriptions.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_quantity_for_an_unknown_item_is_refused()
    {
        var request = NewRequest();
        request.Quantities[0].ItemKey = "not-an-item";

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_quantity_invalid");
    }

    [Fact]
    public async Task An_unknown_time_zone_is_refused_before_anything_is_written()
    {
        var request = NewRequest();
        request.TimeZoneId = "Middle/Earth";

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _subscriptions.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an unusable schedule must not become a stored subscription");
    }

    /// <summary>
    /// A card-free trial takes no money at signup, so the first fee is the day it ends.
    /// </summary>
    [Fact]
    public async Task A_card_free_trial_bills_for_the_first_time_when_it_ends()
    {
        _plan.TrialDays = 14;
        _plan.TrialRequiresPaymentMethod = false;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.NextFeeBillingAtUtc.Should().Be(_created.Trial!.EndsAtUtc);
    }

    /// <summary>
    /// The regression this guards: a trial demanding a card is charged for its first period up
    /// front, because the money path cannot hold a card without charging it. Billing again on
    /// the trial's last day took the same money twice.
    /// </summary>
    [Fact]
    public async Task A_trial_that_demands_a_card_is_not_billed_again_when_it_ends()
    {
        _plan.TrialDays = 14;
        _plan.TrialRequiresPaymentMethod = true;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.NextFeeBillingAtUtc.Should().NotBe(_created.Trial!.EndsAtUtc,
            "the first period was already paid at signup");
        _created.NextFeeBillingAtUtc.Should().Be(_created.CurrentPeriodEndUtc,
            "the next fee falls when the period that was paid for runs out");
    }

    [Fact]
    public async Task A_subscription_without_a_trial_bills_at_the_period_end()
    {
        _plan.TrialDays = null;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.Trial.Should().BeNull();
        _created.NextFeeBillingAtUtc.Should().Be(_created.CurrentPeriodEndUtc);
    }

    [Fact]
    public async Task A_trial_carries_its_own_capped_grants()
    {
        _plan.TrialDays = 14;
        _plan.TrialGrants =
        [
            new TrialMeterGrant { MeterKey = "screening", IncludedQuantity = 25 }
        ];

        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.Trial!.EndsAtUtc.Should().Be(new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc));
        _created.Trial.Grants.Should().ContainSingle()
            .Which.IncludedQuantity.Should().Be(25,
                "each unit costs the seller real money, so an uncapped trial is a direct loss");
    }

    [Fact]
    public async Task An_end_of_calendar_month_trial_ends_at_local_midnight_on_the_first()
    {
        // Signup is 14 August 2026, 10:00 UTC — 12:00 local (Zurich is CEST in August).
        _plan.TrialDays = null;
        _plan.TrialDurationKind = TrialDurationKind.EndOfCalendarMonth;
        _plan.TrialDurationCount = null;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        // 1 September local midnight, still CEST (UTC+2).
        _created!.Trial!.EndsAtUtc.Should().Be(new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc));
        _created.Trial.DurationKind.Should().Be(TrialDurationKind.EndOfCalendarMonth);
        _created.Trial.DurationCount.Should().BeNull();
    }

    [Fact]
    public async Task An_anniversary_months_trial_ends_the_same_local_time_n_months_later()
    {
        _plan.TrialDays = null;
        _plan.TrialDurationKind = TrialDurationKind.AnniversaryMonths;
        _plan.TrialDurationCount = 1;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        // 14 September 2026, 12:00 local — still CEST.
        _created!.Trial!.EndsAtUtc.Should().Be(new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc));
        _created.Trial.DurationKind.Should().Be(TrialDurationKind.AnniversaryMonths);
        _created.Trial.DurationCount.Should().Be(1);
    }

    /// <summary>
    /// A card-free trial anchors the whole later schedule on where it ends — proven here for a
    /// non-day duration mode, since <see cref="A_card_free_trial_bills_for_the_first_time_when_it_ends"/>
    /// only proves it for the legacy day-based one.
    /// </summary>
    [Fact]
    public async Task A_card_free_end_of_calendar_month_trial_anchors_the_fee_schedule_on_its_end()
    {
        _plan.TrialDays = null;
        _plan.TrialDurationKind = TrialDurationKind.EndOfCalendarMonth;
        _plan.TrialRequiresPaymentMethod = false;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.NextFeeBillingAtUtc.Should().Be(_created.Trial!.EndsAtUtc);
    }

    /// <summary>
    /// The regression this guards: only the calendar-aligned branch passed
    /// <c>scheduleAnchorUtc</c> into schedule creation. An anniversary price's
    /// <c>FeeSchedule.AnchorInstantUtc</c> was silently left on the signup instant, so every paid
    /// period after the trial still followed the day the customer signed up rather than the day
    /// the trial actually ended.
    /// </summary>
    [Fact]
    public async Task A_card_free_end_of_calendar_month_trial_anchors_the_anniversary_schedule_itself()
    {
        _plan.TrialDays = null;
        _plan.TrialDurationKind = TrialDurationKind.EndOfCalendarMonth;
        _plan.TrialRequiresPaymentMethod = false;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        var trialEndUtc = _created!.Trial!.EndsAtUtc;
        _created.FeeSchedule.AnchorInstantUtc.Should().Be(trialEndUtc,
            "the schedule itself, not only NextFeeBillingAtUtc, must follow the trial's end");

        BillingPeriodCalculator.TryGetPeriod(_created.FeeSchedule, trialEndUtc, out var first)
            .Should().BeTrue();
        first.StartUtc.Should().Be(trialEndUtc);
        // 1 October local midnight, still CEST (UTC+2).
        first.EndUtc.Should().Be(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc));

        BillingPeriodCalculator.TryGetPeriod(_created.FeeSchedule, first.EndUtc, out var second)
            .Should().BeTrue();
        second.StartUtc.Should().Be(first.EndUtc);
        // 1 November local midnight — CET (UTC+1) by then, DST having ended 25 October.
        second.EndUtc.Should().Be(new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_card_free_anniversary_months_trial_anchors_the_schedule_itself()
    {
        _plan.TrialDays = null;
        _plan.TrialDurationKind = TrialDurationKind.AnniversaryMonths;
        _plan.TrialDurationCount = 1;
        _plan.TrialRequiresPaymentMethod = false;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        // 14 September 2026, 12:00 local (CEST) — one anniversary month after signup.
        var trialEndUtc = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
        _created!.Trial!.EndsAtUtc.Should().Be(trialEndUtc);
        _created.FeeSchedule.AnchorInstantUtc.Should().Be(trialEndUtc);

        BillingPeriodCalculator.TryGetPeriod(_created.FeeSchedule, trialEndUtc, out var first)
            .Should().BeTrue();
        first.StartUtc.Should().Be(trialEndUtc);
        // 14 October, 12:00 local — still CEST (DST ends 25 October).
        first.EndUtc.Should().Be(new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc));

        BillingPeriodCalculator.TryGetPeriod(_created.FeeSchedule, first.EndUtc, out var second)
            .Should().BeTrue();
        second.StartUtc.Should().Be(first.EndUtc);
        // 14 November, 12:00 local — CET (UTC+1) by then.
        second.EndUtc.Should().Be(new DateTime(2026, 11, 14, 11, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// A calendar-aligned price renews on calendar boundaries regardless of when the trial ends —
    /// its schedule anchors to the first of the month the trial ends in, not to the trial-end
    /// instant itself, and the partial month in between is charged as a stub at conversion (see
    /// <c>SubscriptionRenewalService.TryResolveTrialConversion</c>). What must still hold is that
    /// the first charge is deferred to the trial's end, exactly as for an anniversary price.
    /// </summary>
    [Fact]
    public async Task A_card_free_trial_on_a_calendar_aligned_price_still_defers_to_the_trial_s_end()
    {
        var price = NewPrice();
        price.BillingAlignment = BillingAlignment.CalendarMonth;
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(price);
        _plan.TrialDays = null;
        _plan.TrialDurationKind = TrialDurationKind.AnniversaryMonths;
        _plan.TrialDurationCount = 1;
        _plan.TrialRequiresPaymentMethod = false;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        // 14 September 2026, 12:00 local — the same trial end an anniversary price gets.
        var trialEndUtc = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
        _created!.Trial!.EndsAtUtc.Should().Be(trialEndUtc);
        _created.NextFeeBillingAtUtc.Should().Be(trialEndUtc,
            "the first charge must wait for the trial regardless of the price's own alignment");

        // The schedule itself still snaps to the calendar boundary the trial ends inside — 1
        // September local, the month the 14 September trial end falls in — which is what makes
        // this a calendar-aligned price at all.
        _created.FeeSchedule.AnchorInstantUtc.Should().Be(
            new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_second_live_subscription_is_a_conflict()
    {
        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("subscription_already_active");
    }

    [Fact]
    public async Task A_price_belonging_to_another_plan_is_not_found()
    {
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var price = NewPrice();
                price.PlanId = "another-plan";

                return price;
            });

        var result = await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
    }

    [Fact]
    public void The_period_amount_multiplies_quantity_by_the_snapshotted_unit_price()
    {
        var subscription = new SubscriptionDetail
        {
            Price = new PriceSnapshot { QuantityItemKey = "seat", UnitAmountMinor = 8900 },
            QuantityItems =
            [
                new SubscriptionQuantityItem
                {
                    ItemKey = "seat",
                    Quantity = 12,
                    UnitAmountMinor = 8900
                }
            ]
        };

        SubscriptionAmountCalculator.PeriodAmountMinor(subscription)
            .Should().Be(106_800);
    }

    [Fact]
    public void A_discount_can_reach_zero_but_never_go_below_it()
    {
        var subscription = new SubscriptionDetail
        {
            Price = new PriceSnapshot { UnitAmountMinor = 1_000 },
            Discount = new DiscountTerms
            {
                Kind = DiscountKind.FixedAmount,
                AmountMinor = 5_000
            }
        };

        SubscriptionAmountCalculator.PeriodAmountMinor(subscription)
            .Should().Be(0, "a negative charge is a refund, and one must never arrive by " +
                            "arithmetic");
    }

    [Fact]
    public async Task A_paid_subscription_is_refused_while_the_billing_profile_is_incomplete()
    {
        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([nameof(SubscriptionBillingProfile.LegalName)]);

        var result = await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        // Refused before anything is charged, which is the only moment refusing is free. Afterwards
        // the invoice is owed whatever the profile says.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_billing_profile_incomplete");
        result.ValidationErrors!["BillingProfile"]
            .Should().Contain(nameof(SubscriptionBillingProfile.LegalName));
        _created.Should().BeNull();
    }

    [Fact]
    public async Task A_complete_billing_profile_lets_the_subscription_through_and_remembers_who_asked()
    {
        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Whoever starts a subscription is, by acting, somebody an invoice may have to name as its
        // initiator — which is a different person from the profile's billing contact more often than not.
        _billingProfile.Verify(
            guard => guard.RememberInitiatorAsync(
                TenantId,
                OrganizationId,
                "user-1",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private SubscriptionCreationService Service() => new(
        _catalogue.Object,
        _subscriptions.Object,
        _discounts.Object,
        _accounts.Object,
        new CreateSubscriptionRequestValidator(),
        NullLogger<SubscriptionCreationService>.Instance,
        _time,
        billingProfile: _billingProfile.Object);

    private static SubscriptionContext Context() =>
        new(TenantId, OrganizationId, "actor-1", "user-1");

    private static CreateSubscriptionRequest NewRequest() => new()
    {
        PlanCode = "professional",
        PriceId = "price-1",
        TimeZoneId = "Europe/Zurich",
        Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = 12 }]
    };

    private static Plan NewPlan() => new()
    {
        ItemId = "plan-1",
        TenantId = TenantId,
        Code = "professional",
        DisplayName = "Professional",
        Status = CatalogueStatus.Active,
        Version = 3,
        QuantityItems =
        [
            new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat", DefaultQuantity = 1 }
        ],
        Meters =
        [
            new PlanMeter
            {
                MeterKey = "screening",
                UnitLabel = "screening",
                IncludedQuantity = 500,
                ThresholdPercents = [80, 100]
            }
        ],
        Entitlements =
        [
            new PlanEntitlement
            {
                Key = "pep_screening",
                LimitKind = EntitlementLimitKind.Count,
                Limit = 500,
                MeterKey = "screening"
            }
        ]
    };

    private static Price NewPrice() => new()
    {
        ItemId = "price-1",
        TenantId = TenantId,
        PlanId = "plan-1",
        CurrencyCode = "CHF",
        UnitAmountMinor = 8900,
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        QuantityItemKey = "seat",
        Status = CatalogueStatus.Active
    };
}
