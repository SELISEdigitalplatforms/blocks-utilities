using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Charging a renewal, and the dunning state machine that follows a decline.
/// </summary>
public sealed class SubscriptionRenewalServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero));

    private BillingAccount? _account = new()
    {
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        ProviderName = "STRIPE",
        DefaultPaymentMethodId = "pm-1"
    };

    private SubscriptionTransition? _transition;

    public SubscriptionRenewalServiceTests()
    {
        _billingAccounts
            .Setup(repository => repository.GetAsync(
                TenantId, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _account);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, transition, _) => _transition = transition)
            .ReturnsAsync(true);

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<SubscriptionChargeRequest, string, string, CancellationToken>(
                (request, _, _, _) => _charge = request)
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
    }

    private SubscriptionChargeRequest? _charge;

    [Fact]
    public async Task A_successful_renewal_advances_the_period_and_clears_dunning()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.DunningAttemptCount = 2;
        subscription.PastDueSinceUtc = _time.GetUtcNow().UtcDateTime.AddDays(-3);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.Active);
        _transition.DunningAttemptCount.Should().Be(0);
        _transition.ClearPastDueSinceAt.Should().BeTrue();
        _transition.LastRenewalPaymentDetailId.Should().Be("pay-1");
        _transition.Event!.EventType.Should().Be(SubscriptionConstants.SubscriptionRenewed);
    }

    [Fact]
    public async Task A_successful_renewal_drops_the_cached_entitlement()
    {
        await Service().RenewAsync(NewSubscription(SubscriptionStatus.Active), CancellationToken.None);

        _cache.Verify(cache => cache.Invalidate(TenantId, OrganizationId), Times.Once);
    }

    [Fact]
    public async Task A_successful_renewal_writes_the_credit_balance_decremented_by_what_it_consumed()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.CreditBalanceMinor = 3_000;

        await Service().RenewAsync(subscription, CancellationToken.None);

        // The period costs 8,900; the full 3,000 credit is consumed and the transition banks
        // what remains — nothing, in this case, since the credit is smaller than the charge.
        _transition!.CreditBalanceMinor.Should().Be(0);
    }

    [Fact]
    public async Task A_first_decline_moves_active_to_past_due()
    {
        Decline();

        var subscription = NewSubscription(SubscriptionStatus.Active);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Active);
        _transition.NewStatus.Should().Be(SubscriptionStatus.PastDue);
        _transition.DunningAttemptCount.Should().Be(1);
        _transition.PastDueSinceUtc.Should().NotBeNull();
        _transition.Event!.EventType.Should().Be(SubscriptionConstants.SubscriptionPastDue);
    }

    [Fact]
    public async Task A_retry_short_of_the_ceiling_stays_past_due()
    {
        Decline();

        var subscription = NewSubscription(SubscriptionStatus.PastDue);
        subscription.DunningAttemptCount = 1;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.PastDue);
        _transition.DunningAttemptCount.Should().Be(2);
        _transition.Event!.EventType.Should().Be(SubscriptionConstants.SubscriptionRenewalFailed);
    }

    [Fact]
    public async Task A_decline_at_the_attempt_ceiling_moves_to_unpaid()
    {
        Decline();

        var subscription = NewSubscription(SubscriptionStatus.PastDue);
        subscription.DunningAttemptCount = 3;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.Unpaid);
        _transition.ClearPastDueSinceAt.Should().BeTrue();
        _transition.ClearNextFeeBillingAt.Should().BeTrue();
        _transition.Event!.EventType.Should().Be(SubscriptionConstants.SubscriptionUnpaid);
    }

    [Fact]
    public async Task No_stored_payment_method_skips_straight_to_unpaid_with_no_attempts()
    {
        _account = new BillingAccount
        {
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            ProviderName = "STRIPE",
            DefaultPaymentMethodId = null
        };

        var subscription = NewSubscription(SubscriptionStatus.Active);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.Unpaid);
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "retrying without a card to charge is pointless");
    }

    [Fact]
    public async Task A_trial_with_a_card_converts_to_active_on_success()
    {
        var subscription = NewSubscription(SubscriptionStatus.Trialing);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Trialing);
        _transition.NewStatus.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task A_trial_with_no_card_converts_straight_to_unpaid()
    {
        _account = new BillingAccount
        {
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            ProviderName = "STRIPE",
            DefaultPaymentMethodId = null
        };

        var subscription = NewSubscription(SubscriptionStatus.Trialing);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.Unpaid);
    }

    [Fact]
    public async Task Already_unpaid_is_left_alone()
    {
        _account = new BillingAccount
        {
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            ProviderName = "STRIPE",
            DefaultPaymentMethodId = null
        };

        var subscription = NewSubscription(SubscriptionStatus.Unpaid);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_successful_renewal_refuses_to_land_on_an_unresolved_quantity_reservation()
    {
        // The in-memory check in the sweep covers the ordinary case. This closes the gap between
        // reading the subscription and writing the transition, where a request arriving in between
        // can take a reservation the renewal has already priced without.
        var subscription = NewSubscription(SubscriptionStatus.Active);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<SubscriptionTransition>(transition =>
                    transition.NewStatus == SubscriptionStatus.Active &&
                    transition.RequireNoSettlementReservation),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_fully_discounted_period_renews_without_charging_anything()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.Discount = new DiscountTerms
        {
            Kind = DiscountKind.FixedAmount,
            AmountMinor = 100_000
        };

        await Service().RenewAsync(subscription, CancellationToken.None);

        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Active);
    }

    private void Decline() =>
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected,
                "card_declined",
                "The card was declined.",
                "corr-1"));

    private SubscriptionRenewalService Service(ISubscriptionWorkScheduler? scheduler = null) => new(
        _subscriptions.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new OptionsStub(),
        NullLogger<SubscriptionRenewalService>.Instance,
        _time,
        audit: null,
        scheduler: scheduler);

    /// <summary>
    /// A decrease is not refunded, so it waits for the period it was scheduled against to close.
    /// The renewal that closes it is the first one priced at the smaller quantity.
    /// </summary>
    [Fact]
    public async Task A_renewal_applies_a_decrease_scheduled_for_the_period_it_is_closing()
    {
        var subscription = WithScheduledDecrease();

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.QuantityItems!.Single().Quantity.Should().Be(4);
        _transition.ClearPendingQuantityChange.Should().BeTrue(
            "applying the quantity and forgetting the schedule must be one write, or the next " +
            "renewal applies it again");
    }

    [Fact]
    public async Task A_renewal_charges_the_smaller_quantity_and_its_band()
    {
        var subscription = WithScheduledDecrease();

        await Service().RenewAsync(subscription, CancellationToken.None);

        // 4 users at CHF 145 falls back to the 0% band: CHF 580.00, not the 5 x 95% it was on.
        _charge!.AmountMinor.Should().Be(58_000);
    }

    [Fact]
    public async Task A_decrease_scheduled_beyond_this_period_is_left_alone()
    {
        var subscription = WithScheduledDecrease();
        subscription.PendingQuantityChange!.EffectiveAtUtc =
            subscription.CurrentPeriodEndUtc.AddMonths(1);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.QuantityItems.Should().BeNull();
        _transition.ClearPendingQuantityChange.Should().BeFalse();
    }

    /// <summary>Five users on a 5% band, with a decrease to four waiting for the period to end.</summary>
    private static SubscriptionDetail WithScheduledDecrease()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);

        subscription.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.Price = new PriceSnapshot
        {
            CurrencyCode = "CHF",
            UnitAmountMinor = 14_500,
            QuantityItemKey = "user"
        };
        subscription.Plan = new PlanSnapshot
        {
            Code = "team",
            DisplayName = "Team",
            QuantityItems =
            [
                new PlanQuantityItem
                {
                    ItemKey = "user",
                    UnitLabel = "user",
                    MinQuantity = 1,
                    QuantityDiscountTiers =
                    [
                        new QuantityDiscountTier { MinimumQuantity = 1, MaximumQuantity = 4, DiscountBasisPoints = 0 },
                        new QuantityDiscountTier { MinimumQuantity = 5, MaximumQuantity = 9, DiscountBasisPoints = 500 }
                    ]
                }
            ]
        };
        subscription.QuantityItems =
        [
            new SubscriptionQuantityItem
            {
                ItemKey = "user", UnitLabel = "user", Quantity = 5, UnitAmountMinor = 14_500
            }
        ];
        subscription.PendingQuantityChange = new PendingQuantityChange
        {
            RequestedQuantities =
            [
                new SubscriptionQuantityItem
                {
                    ItemKey = "user", UnitLabel = "user", Quantity = 4, UnitAmountMinor = 14_500
                }
            ],
            EffectiveAtUtc = subscription.CurrentPeriodEndUtc,
            ExpectedVersion = 7
        };

        return subscription;
    }

    [Fact]
    public async Task A_successful_renewal_announces_the_period_that_has_just_become_due()
    {
        // The point of producing where the state changes: nothing has to go looking for this
        // subscription's next renewal, and the key is the period, so the sweep scheduling the same
        // one lands on the same occurrence.
        var scheduler = new Mock<ISubscriptionWorkScheduler>();
        var subscription = NewSubscription(SubscriptionStatus.Active);

        await Service(scheduler.Object).RenewAsync(subscription, CancellationToken.None);

        scheduler.Verify(
            candidate => candidate.ScheduleAsync(
                SubscriptionWorkType.Renewal,
                TenantId,
                It.Is<string>(key => key.StartsWith("renewal:", StringComparison.Ordinal)),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                "sub-1",
                OrganizationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_renewal_that_cannot_announce_its_next_period_is_still_a_renewal()
    {
        // The money moved and the renewal is recorded. Reporting failure because a scheduling write
        // in another database did not land would turn a bookkeeping problem into a renewal that
        // looks unfinished — and the repair sweep exists precisely to find this.
        var scheduler = new Mock<ISubscriptionWorkScheduler>();
        scheduler
            .Setup(candidate => candidate.ScheduleAsync(
                It.IsAny<SubscriptionWorkType>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("root database unreachable"));

        var subscription = NewSubscription(SubscriptionStatus.Active);

        var act = async () =>
            await Service(scheduler.Object).RenewAsync(subscription, CancellationToken.None);

        await act.Should().NotThrowAsync();

        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<SubscriptionTransition>(transition =>
                    transition.NewStatus == SubscriptionStatus.Active),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the renewal itself still landed");
    }

    [Fact]
    public async Task A_declined_renewal_announces_nothing()
    {
        // Dunning owns the retry cadence, and it is already scheduled by the failure path. A new
        // occurrence here would be a second opinion about when to try again.
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "Declined.", "corr-1"));

        var scheduler = new Mock<ISubscriptionWorkScheduler>();

        await Service(scheduler.Object).RenewAsync(
            NewSubscription(SubscriptionStatus.Active), CancellationToken.None);

        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_renewal_charge_carries_the_whole_discount_breakdown()
    {
        // Recorded so an invoice can explain itself later. One combined "something came off" cannot
        // be turned back into which of the three reductions produced it, and that is the question
        // somebody reading a months-old invoice actually has.
        SubscriptionChargeRequest? charged = null;
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((SubscriptionChargeRequest request, string _, string __, CancellationToken ___) =>
                charged = request)
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.Price.UnitAmountMinor = 100_000;
        subscription.Price.AutomaticDiscountBasisPoints = 800;
        subscription.Price.QuantityDiscountCombination = AutomaticDiscountCombination.Additive;
        subscription.Discount = new DiscountTerms
        {
            Code = "extra10",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 1_000
        };
        subscription.Plan.QuantityDiscountCombinationPolicy =
            QuantityDiscountCombinationPolicy.Stack;

        await Service().RenewAsync(subscription, CancellationToken.None);

        charged.Should().NotBeNull();
        charged!.GrossAmountMinor.Should().Be(100_000);
        charged.BuiltInDiscountMinor.Should().Be(8_000);
        charged.PromotionalDiscountMinor.Should().Be(9_200, "10% of what the 8% left");
        charged.AutomaticDiscountBasisPoints.Should().Be(800);
        charged.DiscountCombination.Should().Be("Additive");
        charged.AmountMinor.Should().Be(82_800);
    }

    [Fact]
    public async Task An_undiscounted_renewal_still_states_its_gross()
    {
        // Gross is what tells a reader "nothing came off" apart from "this predates the breakdown".
        SubscriptionChargeRequest? charged = null;
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((SubscriptionChargeRequest request, string _, string __, CancellationToken ___) =>
                charged = request)
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        await Service().RenewAsync(NewSubscription(SubscriptionStatus.Active), CancellationToken.None);

        charged!.GrossAmountMinor.Should().Be(8_900);
        charged.BuiltInDiscountMinor.Should().Be(0);
        charged.PromotionalDiscountMinor.Should().Be(0);
        charged.AutomaticDiscountBasisPoints.Should().BeNull();
        charged.DiscountCombination.Should().BeNull();
    }

    // ---- Recovering an Unpaid trial once a card is finally supplied --------------------------
    //
    // RecoverAsync exists for exactly one moment: a card confirmed against a subscription that
    // already lost access for want of one. What is pinned below is what makes reusing RenewAsync
    // safe for that moment rather than dangerous -- the anchor a late recovery prices against, and
    // what a declined recovery attempt must not do.

    private static SubscriptionDetail NewUnconvertedTrial(DateTime trialEndsAtUtc) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Unpaid,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8_900 },
        // Anniversary billing, not calendar-aligned -- the case that has no stub of its own and
        // therefore no help from TryResolveTrialConversion's calendar-only branch. #353 anchors the
        // schedule at the trial's own end, which is what a correct recovery has to reproduce even
        // when it runs long after that instant.
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = trialEndsAtUtc,
            TimeZoneId = "UTC",
            AnchorDayOfMonth = trialEndsAtUtc.Day
        },
        Trial = new TrialTerms { EndsAtUtc = trialEndsAtUtc, RequiresPaymentMethod = false },
        InitialChargeAmountMinor = null
    };

    [Fact]
    public async Task Recovering_a_trial_that_lapsed_weeks_ago_still_charges_the_period_it_owes()
    {
        // The trial ended 1 January; the card arrives 20 February, seven weeks later. A recovery
        // anchored on "now" would resolve whatever period February falls in and skip the one
        // actually owed -- this is the trap #353 caught for the prompt case and this closes for the
        // late one.
        var trialEndsAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _time.Advance(new DateTimeOffset(2026, 2, 20, 9, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());
        var subscription = NewUnconvertedTrial(trialEndsAtUtc);

        await Service().RecoverAsync(subscription, CancellationToken.None);

        _transition!.CurrentPeriodStartUtc.Should().Be(trialEndsAtUtc,
            "the period owed is the one right after the trial, not whichever one 20 February falls in");
        _transition.CurrentPeriodEndUtc.Should().Be(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        _charge!.AmountMinor.Should().Be(8_900, "a whole period at the quoted price, not a stub");
    }

    [Fact]
    public async Task A_successful_recovery_moves_straight_to_active()
    {
        var subscription = NewUnconvertedTrial(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await Service().RecoverAsync(subscription, CancellationToken.None);

        _transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Unpaid);
        _transition.NewStatus.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task A_declined_recovery_stays_unpaid_rather_than_reaching_pastdue()
    {
        // The trap: PastDue is a live status. ApplyFailureAsync's ordinary branch would have moved
        // any non-PastDue subscription there on a decline, which for Unpaid means granting paid
        // access to somebody whose card was just refused.
        Decline();
        var subscription = NewUnconvertedTrial(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await Service().RecoverAsync(subscription, CancellationToken.None);

        _transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Unpaid);
        _transition.NewStatus.Should().Be(SubscriptionStatus.Unpaid);
        _transition.NewStatus.Should().NotBe(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task A_second_recovery_after_a_decline_is_not_replayed_as_the_first_ones_result()
    {
        // The attempt count is what the charge's idempotency key is derived from. Left unmoved by a
        // declined recovery, a second attempt -- from a genuinely different card the subscriber
        // supplied afterward -- would derive the identical key the first attempt used, and the
        // gateway would hand back the stale decline instead of trying the new card at all.
        Decline();
        var subscription = NewUnconvertedTrial(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await Service().RecoverAsync(subscription, CancellationToken.None);

        _transition!.DunningAttemptCount.Should().Be(
            1, "so the next attempt's idempotency key is not the one this decline already used");
    }

    [Fact]
    public async Task RecoverAsync_refuses_a_subscription_that_is_not_unpaid()
    {
        // Called from exactly one place. Anything reaching this method for a live subscription is
        // a caller mistake, and charging it through the wrong entry point is the failure mode this
        // guard exists to rule out entirely rather than trust every future caller to avoid.
        var subscription = NewSubscription(SubscriptionStatus.Active);

        await Service().RecoverAsync(subscription, CancellationToken.None);

        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static SubscriptionDetail NewSubscription(SubscriptionStatus status) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = status,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8_900 },
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        }
    };

    // ---- Applying a plan change scheduled for this boundary ----------------------------------

    /// <summary>
    /// The boundary a scheduled plan change was booked for installs it, in the one write that
    /// advances the period.
    /// </summary>
    /// <remarks>
    /// All of it together, deliberately: a renewal that installed the plan without its price would
    /// bill the new plan at the old rate, and one that installed both without clearing the schedule
    /// would install them again next period.
    /// </remarks>
    [Fact]
    public async Task A_due_plan_change_is_installed_by_the_renewal_that_opens_its_period()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc = _time.GetUtcNow().UtcDateTime;
        subscription.PendingPlanChange = ScheduledChange(subscription.CurrentPeriodEndUtc);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.Plan!.Code.Should().Be("premium");
        _transition.Price!.UnitAmountMinor.Should().Be(19_900);
        _transition.ClearPendingPlanChange.Should().BeTrue();
        _transition.FeeSchedule.Should().NotBeNull();
        _transition.UsageSchedule.Should().NotBeNull();
    }

    /// <summary>
    /// A monthly-to-annual change opens an annual period, not another monthly one.
    /// </summary>
    /// <remarks>
    /// The period has to be resolved from the schedule being moved <em>onto</em>. Resolved from
    /// the outgoing monthly schedule it would charge the annual price and then persist a period
    /// ending one month later, leaving the subscription due again next month for a year it had
    /// just paid for — money taken twice for the same weeks.
    /// </remarks>
    [Fact]
    public async Task A_monthly_to_annual_change_opens_an_annual_period_not_another_month()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc = _time.GetUtcNow().UtcDateTime;

        var change = ScheduledChange(subscription.CurrentPeriodEndUtc);
        change.FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Year,
            IntervalCount = 1,
            AnchorInstantUtc = subscription.CurrentPeriodEndUtc,
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        };
        subscription.PendingPlanChange = change;

        await Service().RenewAsync(subscription, CancellationToken.None);

        var opened = _transition!.CurrentPeriodEndUtc!.Value - _transition.CurrentPeriodStartUtc!.Value;
        opened.Should().BeCloseTo(TimeSpan.FromDays(365), TimeSpan.FromDays(1));
        _transition.NextFeeBillingAtUtc.Should().Be(_transition.CurrentPeriodEndUtc);
    }

    /// <summary>
    /// The renewal charges the plan being moved onto, not the one being left.
    /// </summary>
    [Fact]
    public async Task A_due_plan_change_is_charged_at_the_target_price()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc = _time.GetUtcNow().UtcDateTime;
        subscription.PendingPlanChange = ScheduledChange(subscription.CurrentPeriodEndUtc);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(19_900);
    }

    /// <summary>
    /// A change booked for a later boundary is left alone by the renewals before it.
    /// </summary>
    [Fact]
    public async Task A_plan_change_booked_for_a_later_boundary_is_not_installed_yet()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc = _time.GetUtcNow().UtcDateTime;
        subscription.PendingPlanChange = ScheduledChange(
            subscription.CurrentPeriodEndUtc.AddYears(1));

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.Plan.Should().BeNull();
        _transition.ClearPendingPlanChange.Should().BeFalse();
    }

    /// <summary>
    /// A declined renewal leaves the change pending and the subscriber on the plan they are paying
    /// for.
    /// </summary>
    /// <remarks>
    /// The same discipline a scheduled quantity change already follows: it is applied on the
    /// success path only. Installing the plan here would leave dunning retrying against a price
    /// nobody has paid for, and would hand over a plan the failed charge did not buy.
    /// </remarks>
    [Fact]
    public async Task A_declined_renewal_leaves_a_due_plan_change_pending()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc = _time.GetUtcNow().UtcDateTime;
        subscription.PendingPlanChange = ScheduledChange(subscription.CurrentPeriodEndUtc);

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "declined", "corr-1"));

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.PastDue);
        _transition.Plan.Should().BeNull();
        _transition.ClearPendingPlanChange.Should().BeFalse();
    }

    /// <summary>
    /// A change that keeps the same metering rhythm does not cut the open usage window short.
    /// </summary>
    [Fact]
    public async Task A_due_plan_change_on_the_same_usage_rhythm_leaves_the_usage_window_alone()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc = _time.GetUtcNow().UtcDateTime;

        var change = ScheduledChange(subscription.CurrentPeriodEndUtc);
        // The rhythm the subscriber already meters on, unchanged by the move.
        subscription.UsageSchedule = change.UsageSchedule;
        subscription.PendingPlanChange = change;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.OutgoingUsagePeriod.Should().BeNull();
    }

    /// <summary>
    /// A change that re-anchors metering freezes the window it is closing, in the same write.
    /// </summary>
    /// <remarks>
    /// Guards the same defect an immediate plan change guards: once <c>UsageSchedule</c> names a
    /// different rhythm, a carry-forward meter's carried-in allowance for the window just closed
    /// can no longer be resolved at all.
    /// </remarks>
    [Fact]
    public async Task A_due_plan_change_that_re_anchors_metering_freezes_the_window_it_closes()
    {
        var subscription = NewSubscription(SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc = _time.GetUtcNow().UtcDateTime;
        subscription.UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        };
        subscription.CurrentUsagePeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentUsagePeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var change = ScheduledChange(subscription.CurrentPeriodEndUtc);
        change.UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Year,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        };
        subscription.PendingPlanChange = change;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.OutgoingUsagePeriod.Should().NotBeNull();
        _transition.OutgoingUsagePeriod!.PeriodStartUtc.Should().Be(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static PendingPlanChange ScheduledChange(DateTime effectiveAtUtc) => new()
    {
        Plan = new PlanSnapshot { Code = "premium", DisplayName = "Premium" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 19_900 },
        QuantityItems = [],
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        },
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        },
        EffectiveAtUtc = effectiveAtUtc
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new() { DunningMaxAttempts = 4 };

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
