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
    private BillingAccount? _account;

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
            .Setup(repository => repository.GetOrCreateAndReconcileAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .Callback<BillingAccount, CancellationToken>((account, _) => _account = account)
            .ReturnsAsync((BillingAccount account, CancellationToken _) => account);

        _billingProfile
            .Setup(guard => guard.ContactDefaultsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingContactDefaults("Ada Byron", "billing@northwind.example"));

        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionDetail, CancellationToken>(
                (subscription, _) => _created = subscription)
            .ReturnsAsync(true);

        // No reservation unless a test says otherwise. Moq's unconfigured default for a
        // Task<SubscriptionDetail?> method is a null Task, not a completed one — awaiting it
        // throws, so every preview test would fail for a reason that has nothing to do with what
        // it is testing without this.
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);
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
    public async Task A_standard_discount_before_its_start_is_refused()
    {
        var request = NewRequest();
        request.DiscountCode = "soon";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "soon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                Terms = new DiscountTerms
                {
                    Code = "soon",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 2_500,
                    StartsAtUtc = _time.GetUtcNow().UtcDateTime.AddMinutes(1)
                }
            });

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_not_started");
        _created.Should().BeNull();
    }

    [Fact]
    public async Task A_standard_discount_is_redeemable_at_its_exact_start_instant()
    {
        var request = NewRequest();
        request.DiscountCode = "now";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "now", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                Terms = new DiscountTerms
                {
                    Code = "now",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 2_500,
                    StartsAtUtc = _time.GetUtcNow().UtcDateTime
                }
            });

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.Discount!.StartsAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime);
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
    /// A trial that demands a card bills for the first time when it ends, exactly as a card-free
    /// one does.
    /// </summary>
    /// <remarks>
    /// This test previously asserted the opposite, and its reason was true when it was written:
    /// the money path could not hold a card without charging it, so a card-required trial paid for
    /// its first period on day one and billing again at the trial's end took the same money twice.
    /// Card setup separates the two — a card is stored and nothing is taken — so the charge that
    /// used to happen at signup now happens once, at the end, and the schedule has to point there.
    /// <para>
    /// The double-charge it guarded against is still guarded, from the other side: nothing is
    /// frozen as an opening charge, so there is no signup payment for the conversion to duplicate.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_trial_that_demands_a_card_bills_for_the_first_time_when_it_ends()
    {
        _plan.TrialDays = 14;
        _plan.TrialRequiresPaymentMethod = true;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.NextFeeBillingAtUtc.Should().Be(_created.Trial!.EndsAtUtc,
            "the first paid period starts when the trial stops");
        _created.InitialChargeAmountMinor.Should().BeNull(
            "nothing was charged at signup, so there is no opening figure to freeze — and the " +
            "renewal path reads this being unset as the trial not having converted yet");
        // Non-nullable on the entity, so "never written" reads as false rather than as absent.
        _created.InitialChargeProrated.Should().BeFalse();
        _created.InitialChargeDiscountApplied.Should().BeFalse();
    }

    /// <summary>
    /// Neither trial mode is charged at signup, which is what makes checkout collect a card
    /// instead of money.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_trial_is_charged_when_it_starts(bool requiresCard)
    {
        _plan.TrialDays = 14;
        _plan.TrialRequiresPaymentMethod = requiresCard;

        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        SubscriptionAmountCalculator.InitialChargeAmountMinor(_created!)
            .Should().Be(0, "a trial that bills on its first day is not a trial");

        // The card is still a condition of starting when the plan asked for one; it is collected
        // by a setup session rather than by taking a payment.
        SubscriptionAmountCalculator.RequiresCardSetup(_created!)
            .Should().Be(requiresCard);
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

    [Fact]
    public async Task A_billing_account_takes_its_contact_from_the_organizations_profile()
    {
        await Service().CreateAsync(NewRequest(), Context(), "corr-1", CancellationToken.None);

        _account!.BillingName.Should().Be("Ada Byron");
        _account.BillingEmail.Should().Be("billing@northwind.example");
    }

    [Fact]
    public async Task An_integration_that_names_its_own_contact_keeps_it()
    {
        var request = NewRequest();
        request.BillingName = "Grace Hopper";
        request.BillingEmail = "grace@contoso.example";

        await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        _account!.BillingName.Should().Be("Grace Hopper");
        _account.BillingEmail.Should().Be("grace@contoso.example");
    }

    [Fact]
    public async Task A_request_naming_only_an_address_still_gets_the_saved_name()
    {
        var request = NewRequest();
        request.BillingEmail = "grace@contoso.example";

        await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        _account!.BillingEmail.Should().Be("grace@contoso.example");
        _account.BillingName.Should().Be("Ada Byron");
    }

    /// <summary>
    /// The identity the preview endpoint exists to guarantee: whatever it quotes is what a
    /// confirming <c>CreateAsync</c> then charges. Run on the same clock, since a real gap between
    /// the two calls is exactly what <c>quoteValidUntilUtc</c> exists to flag rather than what this
    /// test is about.
    /// </summary>
    [Fact]
    public async Task A_preview_quotes_exactly_what_the_confirm_then_charges()
    {
        var service = Service();

        var preview = await service.PreviewAsync(
            NewRequest(), Context(), "corr-preview", CancellationToken.None);
        var created = await service.CreateAsync(
            NewRequest(), Context(), "corr-create", CancellationToken.None);

        preview.IsSuccess.Should().BeTrue();
        created.IsSuccess.Should().BeTrue();

        preview.Value!.TotalDueNowMinor.Should().Be(
            SubscriptionAmountCalculator.InitialChargeAmountMinor(created.Value!));
        preview.Value.CurrencyCode.Should().Be(created.Value!.CurrencyCode);
        preview.Value.PeriodStartUtc.Should().Be(created.Value.CurrentPeriodStartUtc);
        preview.Value.PeriodEndUtc.Should().Be(created.Value.CurrentPeriodEndUtc);
        preview.Value.NextRenewalAtUtc.Should().Be(created.Value.NextFeeBillingAtUtc);
    }

    /// <summary>The same identity, holding under a promotional discount.</summary>
    [Fact]
    public async Task A_preview_with_a_discount_still_quotes_exactly_what_is_charged()
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

        var service = Service();

        var preview = await service.PreviewAsync(
            request, Context(), "corr-preview", CancellationToken.None);
        var created = await service.CreateAsync(
            request, Context(), "corr-create", CancellationToken.None);

        preview.Value!.TotalDueNowMinor.Should().Be(
            SubscriptionAmountCalculator.InitialChargeAmountMinor(created.Value!));
        preview.Value.SubtotalMinor.Should().Be(created.Value!.Price.UnitAmountMinor * 12);
        preview.Value.PromotionalDiscountMinor.Should().BeGreaterThan(0);
        (preview.Value.SubtotalMinor - preview.Value.DiscountMinor + preview.Value.TaxMinor)
            .Should().Be(preview.Value.TotalDueNowMinor,
                "subtotal, less every discount, plus tax has to reconcile to the same total the " +
                "customer is actually charged");
    }

    /// <summary>
    /// Arranges the catalogue price this fixture already resolves to <c>"price-1"</c>, but with a
    /// tax configuration -- the per-test override every other price-shaped test in this file
    /// already uses (see <c>A_price_that_has_been_retired_can_no_longer_be_sold</c>).
    /// </summary>
    private void ArrangeTaxedPrice(int rateBasisPoints, TaxMode mode)
    {
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var price = NewPrice();
                price.TaxRateBasisPoints = rateBasisPoints;
                price.TaxMode = mode;

                return price;
            });
    }

    /// <summary>
    /// Arranges the catalogue price this fixture already resolves to <c>"price-1"</c>, but
    /// calendar-aligned monthly -- the shape a trial ending mid-month needs to buy a stub rather
    /// than a full period at its actual conversion.
    /// </summary>
    private void ArrangeCalendarAlignedMonthlyPrice()
    {
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var price = NewPrice();
                price.BillingAlignment = BillingAlignment.CalendarMonth;

                return price;
            });
    }

    /// <summary>
    /// The bug this pins down: pricing the renewal a trial-bearing preview shows as if it were
    /// charged today, rather than at the trial's own end. A calendar-aligned trial ending
    /// mid-month buys the days left in that month at conversion, not a full one -- and the old
    /// PeriodAmountMinor(subscription, subscription.CreatedAtUtc) call always priced a full period,
    /// because it never resolved the trial's own conversion at all.
    /// </summary>
    /// <remarks>
    /// The prorated stub belongs on <c>NextCharge</c>, never on <c>NextRenewal</c>/
    /// <c>NextRenewalAmountMinor</c> -- those two are documented as the full recurring period and
    /// must keep meaning exactly that for a client already reading them that way. This test pins
    /// down both halves: the new field carries the stub, the existing ones do not move.
    /// </remarks>
    [Fact]
    public async Task A_calendar_aligned_trial_ending_mid_month_previews_the_prorated_stub_as_the_next_charge()
    {
        ArrangeCalendarAlignedMonthlyPrice();
        // 14 August + 42 days = 25 September -- squarely inside a month, not on its boundary.
        _plan.TrialDays = 42;
        _plan.TrialRequiresPaymentMethod = false;

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        const long fullPeriodMinor = 8_900 * 12; // the price's own undiscounted full period.

        // The actual next charge is the prorated stub -- strictly less than a full period.
        result.Value!.NextCharge.Prorated.Should().BeTrue();
        result.Value.NextCharge.CoveredDays.Should().NotBeNull();
        result.Value.NextCharge.TotalDays.Should().NotBeNull();
        result.Value.NextCharge.CoveredDays!.Value.Should().BeLessThan(
            result.Value.NextCharge.TotalDays!.Value);
        result.Value.NextCharge.SubtotalMinor.Should().BeLessThan(fullPeriodMinor,
            "the trial ends mid-month, so the first real charge buys only the days left in it, " +
            "not a full calendar month");
        result.Value.NextCharge.SubtotalMinor.Should().BeGreaterThan(0);
        result.Value.NextCharge.ChargeAtUtc.Should().Be(result.Value.TrialEndsAtUtc!.Value,
            "the stub is charged the instant the trial ends, not on some later boundary");
        result.Value.NextRenewal.RenewalAtUtc.Should().Be(result.Value.NextCharge.PeriodEndUtc,
            "the full recurring price starts only after the conversion stub ends");
        result.Value.NextRenewal.RenewalAtUtc.Should().BeAfter(result.Value.NextCharge.ChargeAtUtc);

        // NextRenewal/NextRenewalAmountMinor keep describing the full recurring period -- the
        // documented, backward-compatible meaning a client reading only those two must still get.
        result.Value.NextRenewal.SubtotalMinor.Should().Be(fullPeriodMinor);
        result.Value.NextRenewalAmountMinor.Should().Be(result.Value.NextRenewal.TotalMinor,
            "the legacy field and its own breakdown must describe the exact same full period");
        result.Value.NextRenewal.TotalMinor.Should().NotBe(result.Value.NextCharge.TotalMinor,
            "the stub and the full period genuinely differ here -- collapsing them back to one " +
            "figure would silently reintroduce the bug this preview exists to have fixed");
    }

    /// <summary>
    /// A promotional code that is still live when the trial starts, but has expired by the time
    /// the trial actually converts weeks later, must not be carried into either renewal figure --
    /// pricing them "as of signup" (the bug) would have kept granting it to both.
    /// </summary>
    [Fact]
    public async Task A_promotional_discount_expiring_before_conversion_does_not_reach_either_renewal_figure()
    {
        _plan.TrialDays = 42; // Converts 25 September -- well after the discount below expires.
        _plan.TrialRequiresPaymentMethod = false;

        var request = NewRequest();
        request.DiscountCode = "earlybird";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "earlybird", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ApplicablePlanCodes = ["professional"],
                ApplicablePriceIds = ["price-1"],
                Terms = new DiscountTerms
                {
                    Code = "earlybird",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 800,
                    // Live at signup (14 August); expired long before the trial's own end.
                    ExpiresAtUtc = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc)
                }
            });

        var result = await Service().PreviewAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        result.Value!.PromotionalDiscountMinor.Should().Be(0,
            "nothing is due now during a trial, whichever discount was accepted");
        result.Value.NextRenewal.PromotionalDiscountMinor.Should().Be(0,
            "the code expired before the trial converts, so pricing the full period at the " +
            "trial's own end must not carry it forward");
        result.Value.NextCharge.PromotionalDiscountMinor.Should().Be(0,
            "nor may the actual next charge, priced at the same conversion instant, carry it");
    }

    [Fact]
    public async Task An_ordinary_next_charge_is_priced_at_its_boundary_not_at_signup()
    {
        ArrangeCalendarAlignedMonthlyPrice();
        var request = NewRequest();
        request.DiscountCode = "short-lived";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "short-lived", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ApplicablePlanCodes = ["professional"],
                ApplicablePriceIds = ["price-1"],
                Terms = new DiscountTerms
                {
                    Code = "short-lived",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 800,
                    ExpiresAtUtc = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc)
                }
            });

        var result = await Service().PreviewAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        result.Value!.PromotionalDiscountMinor.Should().BeGreaterThan(0,
            "the opening charge occurs before the code expires");
        result.Value.NextCharge.ChargeAtUtc.Should().BeAfter(
            new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        result.Value.NextCharge.PromotionalDiscountMinor.Should().Be(0,
            "the actual next charge occurs after the code expires");
    }

    [Fact]
    public async Task A_one_period_discount_consumed_by_the_opening_charge_is_absent_from_next_charge()
    {
        ArrangeCalendarAlignedMonthlyPrice();
        var request = NewRequest();
        request.DiscountCode = "opening-only";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "opening-only", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ApplicablePlanCodes = ["professional"],
                ApplicablePriceIds = ["price-1"],
                Terms = new DiscountTerms
                {
                    Code = "opening-only",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 800,
                    DurationPeriods = 1
                }
            });

        var result = await Service().PreviewAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        result.Value!.PromotionalDiscountMinor.Should().BeGreaterThan(0);
        result.Value.NextCharge.PromotionalDiscountMinor.Should().Be(0,
            "activation consumes the one discounted calendar-aligned opening charge before renewal");
    }

    [Fact]
    public async Task An_anniversary_opening_charge_keeps_the_legacy_discount_period_for_next_charge()
    {
        var request = NewRequest();
        request.DiscountCode = "legacy-anniversary";
        _discounts.Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "legacy-anniversary", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ApplicablePlanCodes = ["professional"],
                ApplicablePriceIds = ["price-1"],
                Terms = new DiscountTerms
                {
                    Code = "legacy-anniversary",
                    Kind = DiscountKind.Percent,
                    PercentBasisPoints = 800,
                    DurationPeriods = 1
                }
            });

        var result = await Service().PreviewAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        result.Value!.NextCharge.PromotionalDiscountMinor.Should().BeGreaterThan(0,
            "anniversary activation deliberately does not consume the opening discount period");
    }

    [Fact]
    public async Task An_exclusive_tax_is_added_on_top_of_the_net_subtotal()
    {
        ArrangeTaxedPrice(810, TaxMode.Exclusive);

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        result.Value!.Tax.Should().NotBeNull();
        result.Value.Tax!.RateBasisPoints.Should().Be(810);
        result.Value.Tax.Mode.Should().Be(nameof(TaxMode.Exclusive));
        result.Value.Tax.AmountMinor.Should().BeGreaterThan(0);
        // No discount in this request, so net subtotal equals the plain subtotal -- and an
        // exclusive tax is charged on top of it, not extracted from it.
        result.Value.NetSubtotalMinor.Should().Be(result.Value.SubtotalMinor);
        result.Value.TotalDueNowMinor.Should().Be(
            result.Value.NetSubtotalMinor + result.Value.Tax.AmountMinor);
        result.Value.TotalDueNowMinor.Should().BeGreaterThan(result.Value.SubtotalMinor,
            "an exclusive tax raises what is actually charged above the subtotal");
    }

    [Fact]
    public async Task An_inclusive_tax_is_extracted_from_the_discounted_total_rather_than_added()
    {
        ArrangeTaxedPrice(810, TaxMode.Inclusive);

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        result.Value!.Tax.Should().NotBeNull();
        result.Value.Tax!.Mode.Should().Be(nameof(TaxMode.Inclusive));
        result.Value.Tax.AmountMinor.Should().BeGreaterThan(0);
        // The configured amount already contains the tax, so what is actually charged is the
        // same subtotal the price names -- not the subtotal plus tax on top.
        result.Value.TotalDueNowMinor.Should().Be(result.Value.SubtotalMinor);
        result.Value.NetSubtotalMinor.Should().Be(
            result.Value.TotalDueNowMinor - result.Value.Tax.AmountMinor);
        result.Value.NetSubtotalMinor.Should().BeLessThan(result.Value.SubtotalMinor,
            "an inclusive tax is found inside the subtotal, which leaves less of it as net");
    }

    [Fact]
    public async Task An_untaxed_price_reports_no_tax_configuration_on_either_breakdown()
    {
        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        result.Value!.Tax.Should().BeNull();
        result.Value.NextRenewal.Tax.Should().BeNull();
    }

    /// <summary>
    /// A card-free trial owes nothing today, but the price it will renew at is still taxed. The
    /// due-now tax must say so -- rate and mode present, amount zero -- rather than reporting
    /// nothing at all, which would read as "this price has no tax" instead of "nothing is due for
    /// it yet."
    /// </summary>
    [Fact]
    public async Task A_card_free_trial_reports_configured_zero_tax_now_and_real_tax_at_renewal()
    {
        ArrangeTaxedPrice(810, TaxMode.Exclusive);
        _plan.TrialDays = null;
        _plan.TrialDurationKind = TrialDurationKind.EndOfCalendarMonth;
        _plan.TrialDurationCount = null;
        _plan.TrialRequiresPaymentMethod = false;

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        result.Value!.TotalDueNowMinor.Should().Be(0);
        result.Value.Tax.Should().NotBeNull();
        result.Value.Tax!.RateBasisPoints.Should().Be(810);
        result.Value.Tax.Mode.Should().Be(nameof(TaxMode.Exclusive));
        result.Value.Tax.AmountMinor.Should().Be(0,
            "nothing is due now, so nothing is taxed now, even though the price carries tax");

        result.Value.NextRenewal.Tax.Should().NotBeNull();
        result.Value.NextRenewal.Tax!.RateBasisPoints.Should().Be(810);
        result.Value.NextRenewal.Tax.AmountMinor.Should().BeGreaterThan(0,
            "the first real renewal, once the trial ends, is taxed like any other charge");
        result.Value.NextRenewal.TotalMinor.Should().Be(result.Value.NextRenewalAmountMinor,
            "the renewal breakdown's own total must agree with the legacy renewal-amount field");
    }

    /// <summary>
    /// The renewal breakdown is read from the exact same <c>PeriodCharge</c> the legacy
    /// <c>NextRenewalAmountMinor</c> already used -- so an ongoing discount reaches both
    /// identically, and the two can never disagree.
    /// </summary>
    [Fact]
    public async Task A_discounted_renewal_exposes_the_exact_tax_and_total_the_calculator_produced()
    {
        ArrangeTaxedPrice(770, TaxMode.Exclusive);
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
                    PercentBasisPoints = 800,
                    // Unbounded, so the discount is still in force at the first renewal, not only
                    // at signup -- otherwise this would only prove the due-now figures agree.
                    DurationPeriods = null
                }
            });

        var result = await Service().PreviewAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "the preview should succeed");
        var renewal = result.Value!.NextRenewal;

        renewal.PromotionalDiscountMinor.Should().BeGreaterThan(0,
            "an unbounded promotional code still reduces the renewal it previews");
        renewal.DiscountMinor.Should().Be(
            renewal.BuiltInDiscountMinor + renewal.PromotionalDiscountMinor);
        renewal.NetSubtotalMinor.Should().Be(renewal.SubtotalMinor - renewal.DiscountMinor);
        renewal.Tax.Should().NotBeNull();
        renewal.Tax!.AmountMinor.Should().BeGreaterThan(0);
        renewal.TotalMinor.Should().Be(renewal.NetSubtotalMinor + renewal.Tax.AmountMinor);
        renewal.TotalMinor.Should().Be(result.Value.NextRenewalAmountMinor,
            "the breakdown's own total must agree with the legacy renewal-amount field -- both " +
            "are read from the same PeriodCharge");
    }

    /// <summary>
    /// <c>BuildPreviewResponse</c> must read tax off the subscription's own already-resolved
    /// <c>PriceSnapshot</c>, never by asking the catalogue a second time -- the same rule every
    /// other figure on this response already follows. Proven here by counting the catalogue read
    /// rather than by racing a mutation against it: a preview is one synchronous call with nothing
    /// concurrent to race, so what actually matters is that there is only ever the one read the
    /// price is resolved from in the first place.
    /// </summary>
    [Fact]
    public async Task The_reported_tax_comes_from_one_already_resolved_price_read_never_a_second_one()
    {
        ArrangeTaxedPrice(770, TaxMode.Exclusive);

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.Value!.Tax!.RateBasisPoints.Should().Be(770);
        _catalogue.Verify(
            repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_preview_writes_nothing()
    {
        await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _accounts.Verify(
            repository => repository.GetOrCreateAndReconcileAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a quote nobody has confirmed must not leave a durable billing account behind");
        _subscriptions.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _billingProfile.Verify(
            guard => guard.RememberInitiatorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "nobody has acted yet, so a preview must not record an initiator");
        _created.Should().BeNull();
    }

    [Fact]
    public async Task A_card_free_calendar_month_trial_previews_the_same_resolved_renewal_boundary()
    {
        _plan.TrialDays = null;
        _plan.TrialDurationKind = TrialDurationKind.EndOfCalendarMonth;
        _plan.TrialDurationCount = null;
        _plan.TrialRequiresPaymentMethod = false;

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalDueNowMinor.Should().Be(0);
        result.Value.TrialEndsAtUtc.Should().Be(
            new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc));
        result.Value.NextRenewalAtUtc.Should().Be(result.Value.TrialEndsAtUtc);
    }

    [Fact]
    public async Task A_plan_requiring_a_card_up_front_previews_that_requirement()
    {
        _plan.RequirePaymentMethodUpfront = true;
        _plan.TrialDays = 14;
        _plan.TrialRequiresPaymentMethod = false;

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.Value!.RequiresCardSetup.Should().BeTrue();
    }

    [Fact]
    public async Task An_organization_that_already_has_a_live_subscription_is_quoted_with_a_blocker()
    {
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDetail { ItemId = "existing" });

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        // A price, not a failure: the customer learns both the amount and what stands in the way,
        // rather than only being told no.
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalDueNowMinor.Should().BeGreaterThan(0);
        result.Value.Blockers.Should().ContainSingle()
            .Which.Code.Should().Be("subscription_already_active");
    }

    [Fact]
    public async Task An_incomplete_checkout_left_over_is_also_a_blocker()
    {
        // The same condition the unique index refuses a real signup for — an abandoned checkout,
        // not yet live, still occupies the one reservation an organization gets.
        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDetail { ItemId = "abandoned" });

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.Value!.Blockers.Should().ContainSingle()
            .Which.Code.Should().Be("subscription_already_active");
    }

    [Fact]
    public async Task An_incomplete_billing_profile_is_a_blocker_not_a_failure_on_preview()
    {
        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([nameof(SubscriptionBillingProfile.LegalName)]);

        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalDueNowMinor.Should().BeGreaterThan(0);

        var blocker = result.Value.Blockers.Should().ContainSingle().Which;
        blocker.Code.Should().Be("subscription_billing_profile_incomplete");
        blocker.Fields!["BillingProfile"]
            .Should().Contain(nameof(SubscriptionBillingProfile.LegalName));
    }

    [Fact]
    public async Task An_unknown_plan_fails_the_preview_exactly_as_it_fails_the_confirm()
    {
        var request = NewRequest();
        request.PlanCode = "not-a-plan";

        var result = await Service().PreviewAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_plan_not_found");
    }

    [Fact]
    public async Task An_unknown_discount_code_fails_the_preview_too()
    {
        var request = NewRequest();
        request.DiscountCode = "missing";

        var result = await Service().PreviewAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_not_found");
    }

    [Fact]
    public async Task A_prorated_quote_states_when_it_stops_holding()
    {
        // Mid-month against a monthly price with no calendar alignment: whole-period pricing, not
        // a stub, so nothing here is prorated and there is no boundary to name.
        var result = await Service().PreviewAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.Value!.Prorated.Should().BeFalse();
        result.Value.QuoteValidUntilUtc.Should().BeNull(
            "a flat price quoted today prices the same tomorrow");
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
