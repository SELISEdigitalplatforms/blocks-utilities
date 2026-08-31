using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>Moving a live subscription to a different price, mid-period, with proration.</summary>
public sealed class SubscriptionPlanChangeServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly Mock<ISubscriptionBillingProfileGuard> _billingProfile = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = NewSubscription(SubscriptionStatus.Active, 1_000);
    private SettlementReservation? _reserved;

    /// <summary>What a change classified as NextRenewal booked, when one was.</summary>
    private PendingPlanChange? _scheduled;
    private BillingAccount? _account = new()
    {
        ItemId = "acct-1",
        ProviderName = "STRIPE",
        DefaultPaymentMethodId = "pm-1",
        ProviderCustomerId = "cus_123"
    };

    public SubscriptionPlanChangeServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        // A change classified as NextRenewal is scheduled rather than applied. Unconfigured, Moq
        // returns false here and every scheduled change would read as a version conflict.
        _subscriptions
            .Setup(repository => repository.TrySetPendingPlanChangeAsync(
                TenantId, "sub-1", It.IsAny<int>(),
                It.IsAny<PendingPlanChange>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, int _, PendingPlanChange pending, CancellationToken _) =>
                _scheduled = pending)
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryReserveSettlementAsync(
                TenantId, "sub-1", It.IsAny<int>(),
                It.IsAny<SettlementReservation>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, int _, SettlementReservation taken, CancellationToken _) =>
                _reserved = taken)
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryReleaseSettlementAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                TenantId,
                "sub-1",
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<PlanSnapshot>(),
                It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(),
                It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(),
                It.IsAny<long>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()))
            .ReturnsAsync(true);

        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "premium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPlan());

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(2_000));

        _billingAccounts
            .Setup(repository => repository.GetAsync(
                TenantId, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _account);

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("in_1", "corr-1"));

        // Complete unless a test says otherwise: the gate is not what most of these are about, and
        // a default of "incomplete" would make every unrelated test fail for the wrong reason.
        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task An_upgrade_charges_the_prorated_difference_through_the_gateway()
    {
        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.Is<SubscriptionChargeRequest>(request => request.AmountMinor == 1_000),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_declined_charge_leaves_the_subscription_unchanged()
    {
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "declined", "corr-1"));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(),
                It.IsAny<string?>(), It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A downgrade banks nothing, so it records no credit note either.
    /// </summary>
    /// <remarks>
    /// This used to assert the opposite: the credit note had to ride the same write as the
    /// transition, because nothing else could reconstruct it afterwards. There is now no credit to
    /// document — the subscriber keeps the period they paid for and the balance does not move — so
    /// the write carries no document source at all.
    /// <para>
    /// <see cref="SubscriptionFinancialDocumentIssuerTests"/> still covers issuing a banked-credit
    /// note, for the sources written before this policy changed and not yet drained.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_downgrade_records_no_credit_note_because_it_banks_nothing()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        SubscriptionDocumentSource? carried = null;
        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()))
            .Callback((string _, string _, int _, string? _, PlanSnapshot _, PriceSnapshot _,
                    List<SubscriptionQuantityItem> _, SubscriptionPlanSchedule _,
                    PendingUsagePeriod _, long _, string? _, SubscriptionOutboxEvent _,
                    CancellationToken _, SubscriptionDocumentSource? source) => carried = source)
            .ReturnsAsync(true);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Nothing applied and nothing documented: the change is booked for the boundary, and no
        // money moved in either direction today.
        carried.Should().BeNull();
        _scheduled.Should().NotBeNull();
        _scheduled!.Plan.Code.Should().Be("premium");
        _scheduled.EffectiveAtUtc.Should().Be(_subscription.CurrentPeriodEndUtc);
    }

    /// <summary>
    /// Guards the P1 finding fixed alongside the overflow hardening: a plan change must freeze a
    /// carry-forward meter's actual carried-in allowance onto the outgoing window before the
    /// schedule swap re-anchors UsageSchedule — a live resolve against the new schedule afterward
    /// cannot see the old anchor's carried allowance at all (see MeterAllowance.CarriedIn's own
    /// remarks on why a window before the current anchor never carries).
    /// </summary>
    /// <remarks>
    /// Uses a non-zero <c>CarryForwardCap</c> and a genuinely partial previous window (80 of 100
    /// used) so the carried allowance itself is non-zero (20) — a cap of zero would be rejected by
    /// <c>PlanDefinitionRequestValidator</c> and would not exercise the carry math at all.
    /// </remarks>
    [Fact]
    public async Task An_immediate_change_freezes_the_carried_forward_allowance_before_the_schedule_re_anchors()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _subscription.UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        };
        _subscription.CurrentUsagePeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        _subscription.CurrentUsagePeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        _subscription.Plan.Meters =
        [
            new PlanMeter
            {
                MeterKey = "screening",
                IncludedQuantity = 100,
                ResetPolicy = MeterResetPolicy.CarryForward,
                CarryForwardCap = 50,
                OverageAllowed = true
            }
        ];
        // An upgrade, so the change still applies immediately and still re-anchors the schedule.
        // A downgrade no longer reaches this path at all -- it is scheduled for the paid period's
        // end, and the equivalent freeze at that boundary is the renewal's own.
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(4_000));

        var usage = new Mock<ISubscriptionUsageRepository>();
        usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter
            {
                MeterKey = "screening",
                Balance = 80,
                LimitSnapshot = 100
            });

        PendingUsagePeriod? captured = null;
        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()))
            .Callback((string _, string _, int _, string? _, PlanSnapshot _, PriceSnapshot _,
                    List<SubscriptionQuantityItem> _, SubscriptionPlanSchedule _,
                    PendingUsagePeriod pending, long _, string? _, SubscriptionOutboxEvent _,
                    CancellationToken _, SubscriptionDocumentSource? _) => captured = pending)
            .ReturnsAsync(true);

        var result = await ServiceWithUsage(usage.Object).ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.MeterAllowances.Should().NotBeNull(
            "an allowance/usage repository was supplied, so the snapshot must be captured");
        captured.MeterAllowances!["screening"].Should().Be(120,
            "the plan's own 100 plus the 20 actually carried forward — 80 used of a 100 limit, " +
            "under the 50 cap — captured against the old anchor before the plan change installs " +
            "a new UsageSchedule that could no longer see it");
    }

    [Fact]
    public async Task A_downgrade_never_calls_the_gateway()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_trial_swap_never_calls_the_gateway_or_touches_credit()
    {
        _subscription = NewSubscription(SubscriptionStatus.Trialing, 1_000);
        _subscription.CreditBalanceMinor = 0;

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(),
                It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(),
                0L,
                It.IsAny<string?>(), It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_different_currency_is_refused()
    {
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(2_000, currencyCode: "EUR"));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_plan_change_currency_mismatch");
    }

    [Fact]
    public async Task A_different_billing_interval_rebuilds_the_fee_schedule()
    {
        var price = NewPrice(2_000);
        price.Interval = BillingInterval.Year;
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(price);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _subscription.FeeSchedule.Interval.Should().Be(BillingInterval.Year);
    }

    [Fact]
    public async Task A_price_restricted_promotion_refuses_a_move_it_does_not_cover()
    {
        // The hole this closes: applicability was checked once, at redemption, and a plan change kept
        // the discount without asking again — so a code sold as monthly-only went on reducing the
        // annual price it was never offered for.
        _subscription.Discount = new DiscountTerms
        {
            Code = "monthly8",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 800,
            ApplicablePriceIds = ["price-monthly"]
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_not_applicable");
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _subscriptions.Verify(repository => repository.TryChangePlanAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
            It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
            It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(), It.IsAny<string?>(),
            It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_plan_restricted_promotion_refuses_a_move_to_another_plan()
    {
        _subscription.Discount = new DiscountTerms
        {
            Code = "basiconly",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 800,
            ApplicablePlanCodes = ["basic"]
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_not_applicable");
    }

    [Fact]
    public async Task A_promotion_that_covers_the_target_moves_with_the_subscriber()
    {
        // The other half: a restriction naming where they are going must not block them.
        _subscription.Discount = new DiscountTerms
        {
            Code = "premium8",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 800,
            ApplicablePlanCodes = ["premium"],
            ApplicablePriceIds = ["price-2"]
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_unrestricted_promotion_moves_with_the_subscriber()
    {
        // Every discount authored before either restriction existed is this shape. A plan change must
        // not start refusing them.
        _subscription.Discount = new DiscountTerms
        {
            Code = "anything",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 800
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_spent_promotion_does_not_block_a_move_it_no_longer_pays_for()
    {
        // Three months of "8% off", all three used. It reduces nothing now, so enforcing where it
        // could once have been redeemed would be blocking a plan change over an offer that has ended.
        _subscription.Discount = new DiscountTerms
        {
            Code = "monthly8",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 800,
            DurationPeriods = 3,
            ApplicablePriceIds = ["price-monthly"]
        };
        _subscription.DiscountPeriodsApplied = 3;

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_expired_promotion_does_not_block_a_move_either()
    {
        _subscription.Discount = new DiscountTerms
        {
            Code = "monthly8",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 800,
            ExpiresAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ApplicablePriceIds = ["price-monthly"]
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_reserved_change_records_how_its_charge_was_arrived_at()
    {
        // Recorded on the reservation, which is what a replay repeats and what the payment record is
        // built from. Recomputing it when the charge settles would price it at a different instant,
        // and possibly against an edited catalogue — an explanation of a charge nobody was quoted.
        var price = NewPrice(2_000);
        price.AutomaticDiscountBasisPoints = 800;
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(price);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _reserved.Should().NotBeNull();

        var settlement = _reserved!.Settlement;
        settlement.Should().NotBeNull();
        settlement!.Outgoing.GrossAmountMinor.Should().Be(1_000);
        settlement.Outgoing.BuiltInDiscountMinor.Should().Be(0);
        settlement.Target.GrossAmountMinor.Should().Be(2_000);
        settlement.Target.BuiltInDiscountMinor.Should().Be(160, "8% of the target price");
        settlement.NetSettlementMinor.Should().Be(_reserved.ChargeAmountMinor);
    }

    [Fact]
    public async Task A_plan_change_charges_under_an_order_id_that_says_so()
    {
        // Both settlement kinds used to share the "quantity:" form, so invoice history classified a
        // plan-change invoice as a renewal and handed the client a reservation id where a period key
        // belongs. The id is what history reads, so this is where the fix has to hold.
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

        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        charged!.OrderId.Should().Be(SubscriptionConstants.SettlementOrderIdFor(
            "sub-1", SettlementReservationKind.PlanChange, _reserved!.ReservationId));
        // Named through the constants rather than spelled out, so shortening a segment cannot leave
        // this test asserting something the code no longer writes.
        charged.OrderId.Should().Contain(
            $":{SubscriptionConstants.PlanChangeSegment}:");
        charged.OrderId.Should().NotContain(
            $":{SubscriptionConstants.QuantitySegment}:",
            "this is not a quantity change, and history reads the kind out of this string");
    }

    [Fact]
    public async Task The_charge_is_still_keyed_on_the_reservation_alone()
    {
        // The order id gained the kind; the *idempotency* key deliberately did not. A reservation
        // taken before that change and replayed after it has to find its own attempt rather than
        // raising a second charge, and the key is what finds it.
        string? key = null;
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((SubscriptionChargeRequest _, string idempotencyKey, string __, CancellationToken ___) =>
                key = idempotencyKey)
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        key.Should().Be(
            SubscriptionConstants.SettlementChargeKeyFor("sub-1", _reserved!.ReservationId));
    }

    [Fact]
    public async Task The_settlement_charge_carries_the_reservations_breakdown()
    {
        // One hop further: the charge the gateway records has to be the reservation's own account of
        // itself, not a fresh calculation.
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

        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        charged.Should().NotBeNull();
        charged!.Settlement.Should().BeSameAs(_reserved!.Settlement);

        // And none of the renewal-shaped fields, which would describe this charge as a discounted
        // price rather than a difference between two of them.
        charged.GrossAmountMinor.Should().Be(0);
        charged.BuiltInDiscountMinor.Should().Be(0);
    }

    [Fact]
    public async Task A_change_snapshots_the_target_prices_automatic_discount()
    {
        // Moving onto the yearly price is how a subscriber gets its 8%. The snapshot is what makes it
        // theirs to keep: clearing the catalogue's discount afterwards must not raise their renewal.
        var price = NewPrice(2_000);
        price.AutomaticDiscountBasisPoints = 800;
        price.QuantityDiscountCombination = AutomaticDiscountCombination.Additive;
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(price);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _subscriptions.Verify(repository => repository.TryChangePlanAsync(
            TenantId,
            "sub-1",
            It.IsAny<int>(),
            It.IsAny<string?>(),
            It.IsAny<PlanSnapshot>(),
            It.Is<PriceSnapshot>(snapshot =>
                snapshot.AutomaticDiscountBasisPoints == 800 &&
                snapshot.QuantityDiscountCombination == AutomaticDiscountCombination.Additive),
            It.IsAny<List<SubscriptionQuantityItem>>(),
            It.IsAny<SubscriptionPlanSchedule>(),
            It.IsAny<PendingUsagePeriod>(),
            It.IsAny<long>(),
            It.IsAny<string?>(),
            It.IsAny<SubscriptionOutboxEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Annual_to_monthly_rebuilds_the_fee_schedule_in_the_other_direction()
    {
        _subscription.Price.Interval = BillingInterval.Year;
        _subscription.FeeSchedule.Interval = BillingInterval.Year;
        _subscription.CurrentPeriodEndUtc = new DateTime(2027, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _subscription.FeeSchedule.Interval.Should().Be(BillingInterval.Month);
        _subscription.CurrentPeriodStartUtc.Should().Be(_time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task A_change_atomically_queues_the_outgoing_usage_window_for_rating()
    {
        _subscription.CurrentUsagePeriodStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        _subscription.CurrentUsagePeriodEndUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        _subscription.Plan.Meters = [new PlanMeter { MeterKey = "requests", IncludedQuantity = 100 }];

        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        _subscriptions.Verify(repository => repository.TryChangePlanAsync(
            TenantId,
            "sub-1",
            It.IsAny<int>(),
            It.IsAny<string?>(),
            It.IsAny<PlanSnapshot>(),
            It.IsAny<PriceSnapshot>(),
            It.IsAny<List<SubscriptionQuantityItem>>(),
            It.IsAny<SubscriptionPlanSchedule>(),
            It.Is<PendingUsagePeriod>(period =>
                period.PeriodStartUtc == new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc) &&
                period.Plan.Meters[0].IncludedQuantity == 100),
            It.IsAny<long>(),
            It.IsAny<string?>(),
            It.IsAny<SubscriptionOutboxEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Unpaid)]
    [InlineData(SubscriptionStatus.Canceled)]
    public async Task An_ineligible_status_is_a_conflict(SubscriptionStatus status)
    {
        _subscription = NewSubscription(status, 1_000);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
    }

    [Fact]
    public async Task A_paid_change_is_charged_under_its_reservation_rather_than_its_version()
    {
        string? key = null;

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((SubscriptionChargeRequest _, string k, string _, CancellationToken _) => key = k)
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        // Keyed on the version, a write lost to a concurrent change left the money moved and the
        // plan unchanged — and the retry, which necessarily read a new version, charged again.
        _reserved.Should().NotBeNull();
        key.Should().Be(
            SubscriptionConstants.SettlementChargeKeyFor("sub-1", _reserved!.ReservationId));
    }

    [Fact]
    public async Task A_paid_change_is_applied_by_promoting_the_reservation_it_charged_under()
    {
        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        // Addressed by the reservation, not the version: the money has moved, so a concurrent
        // change bumping the version must not be able to strand terms already paid for.
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                TenantId,
                "sub-1",
                It.IsAny<int>(),
                _reserved!.ReservationId,
                It.IsAny<PlanSnapshot>(),
                It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(),
                It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(),
                It.IsAny<long>(),
                "in_1",
                It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()),
            Times.Once);
    }

    [Fact]
    public async Task The_reservation_carries_the_terms_the_customer_was_quoted()
    {
        // Held in full so a promotion by the sweep delivers what was paid for, even if the
        // catalogue has been edited in the meantime.
        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        _reserved!.Kind.Should().Be(SettlementReservationKind.PlanChange);
        _reserved.PlanChange.Should().NotBeNull();
        _reserved.PlanChange!.Plan.Code.Should().Be("premium");
        _reserved.PlanChange.Schedule.Should().NotBeNull();
        _reserved.StoredPaymentMethodId.Should().Be("pm-1");
    }

    [Theory]
    [InlineData(PaymentFailureKind.Timeout)]
    [InlineData(PaymentFailureKind.Unavailable)]
    public async Task An_unanswered_change_keeps_its_reservation_rather_than_inviting_a_retry(
        PaymentFailureKind kind)
    {
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                kind, "provider_unreachable", "No answer.", "corr-1"));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_plan_change_charge_unresolved");
        result.FailureKind.Should().Be(kind);
        _subscriptions.Verify(
            repository => repository.TryReleaseSettlementAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "an unanswered charge may have been collected");
    }

    [Fact]
    public async Task A_declined_change_releases_its_reservation()
    {
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "Declined.", "corr-1"));

        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.TryReleaseSettlementAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_change_with_nothing_to_charge_takes_no_reservation()
    {
        // A downgrade moves no money, so there is nothing to settle and nothing to lock — and it
        // is now scheduled rather than applied, so it does not touch the plan either.
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);

        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.TryReserveSettlementAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<SettlementReservation>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()),
            Times.Never,
            "a downgrade waits for the paid period to end rather than applying now");

        _scheduled.Should().NotBeNull();
    }

    [Fact]
    public async Task A_quantity_increase_mid_settlement_blocks_a_plan_change()
    {
        // The increase has reserved units priced against the plan being left, and its promotion
        // writes them by claim id rather than by version — so it would land on the new plan.
        _subscription.SettlementReservation = new SettlementReservation
        {
            ReservationId = "reservation-1",
            Kind = SettlementReservationKind.QuantityIncrease,
            QuantityChange = new ReservedQuantityChange { RequestedQuantities = [] },
            ChargeAmountMinor = 5_437,
            ReservedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc)
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_quantity_change_in_flight");
        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()),
            Times.Never);
    }

    [Fact]
    public async Task A_lost_compare_and_set_is_reported_as_a_conflict()
    {
        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(),
                It.IsAny<string?>(), It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_plan_change_conflict");
    }

    [Fact]
    public async Task No_payment_method_refuses_an_upgrade()
    {
        _account = new BillingAccount
        {
            ItemId = "acct-1",
            ProviderName = "STRIPE",
            DefaultPaymentMethodId = null
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_plan_change_no_payment_method");
    }

    [Fact]
    public async Task A_requested_organization_is_forwarded_to_context_resolution()
    {
        var request = Request();
        request.OrganizationId = "org-9";

        await Service().ChangePlanAsync("sub-1", request, "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    /// <summary>
    /// The identity a plan-change preview promises: the same math the confirm evaluates, on the
    /// same clock. Unlike the purchase preview, nothing here is frozen — this only holds because
    /// both calls share one <see cref="ControlledTimeProvider"/> reading, exactly as it would hold
    /// for two calls made in the same instant in production.
    /// </summary>
    [Fact]
    public async Task A_preview_quotes_exactly_what_the_change_then_charges()
    {
        var service = Service();

        var preview = await service.PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-preview", CancellationToken.None);
        var applied = await service.ChangePlanAsync(
            "sub-1", Request(), "corr-apply", CancellationToken.None);

        preview.IsSuccess.Should().BeTrue();
        applied.IsSuccess.Should().BeTrue();

        preview.Value!.ChargeMinor.Should().Be(1_000);
        _reserved!.ChargeAmountMinor.Should().Be(preview.Value.ChargeMinor);
        preview.Value.CreditBankedMinor.Should().Be(0);
        preview.Value.CurrencyCode.Should().Be("CHF");
        preview.Value.TargetPlanCode.Should().Be("premium");
    }

    /// <summary>
    /// A downgrade preview quotes nothing due and nothing banked.
    /// </summary>
    /// <remarks>
    /// <c>CreditBankedMinor</c> is kept on the response for callers that already read it, but no
    /// change banks credit any more, so it is now always zero. It is deprecated rather than
    /// removed: dropping a field from a response breaks a client that reads it, where a field that
    /// is always zero merely stops being interesting.
    /// </remarks>
    [Fact]
    public async Task A_preview_of_a_downgrade_reports_nothing_due_and_nothing_banked()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        var service = Service();

        var preview = await service.PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-preview", CancellationToken.None);
        var applied = await service.ChangePlanAsync(
            "sub-1", Request(), "corr-apply", CancellationToken.None);

        preview.Value!.ChargeMinor.Should().Be(0);
        preview.Value.CreditBankedMinor.Should().Be(0);
        applied.Value!.CurrencyCode.Should().Be("CHF");
    }

    [Fact]
    public async Task A_preview_writes_nothing()
    {
        await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.TryReserveSettlementAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<SettlementReservation>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()),
            Times.Never);
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _billingProfile.Verify(
            guard => guard.RememberInitiatorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "nobody has acted yet, so a preview must not record an initiator");
    }

    [Fact]
    public async Task An_in_flight_settlement_reservation_does_not_block_a_preview()
    {
        // The check that refuses ChangePlanAsync here (A_quantity_increase_mid_settlement_blocks_a_
        // plan_change) is read-only-safe to skip on a preview, which writes nothing and does not
        // need the reservation clear to quote a price.
        _subscription.SettlementReservation = new SettlementReservation
        {
            ReservationId = "reservation-1",
            Kind = SettlementReservationKind.QuantityIncrease,
            QuantityChange = new ReservedQuantityChange { RequestedQuantities = [] },
            ChargeAmountMinor = 5_437,
            ReservedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc)
        };

        var result = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ChargeMinor.Should().Be(1_000);
    }

    [Fact]
    public async Task A_pending_annual_period_does_not_block_a_preview()
    {
        _subscription.PendingAnnualPeriod = new PendingAnnualPeriod
        {
            StartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var result = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// The one refusal that stays a hard failure on preview rather than becoming a blocker: the
    /// real change never charges a price with the discount silently dropped, so there is no
    /// honest number to show alongside the refusal.
    /// </summary>
    [Fact]
    public async Task An_unsurvivable_discount_fails_the_preview_exactly_as_it_fails_the_change()
    {
        _subscription.Discount = new DiscountTerms
        {
            Code = "loyal",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 1_000,
            ApplicablePlanCodes = ["basic"]
        };

        var result = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_not_applicable");
    }

    [Fact]
    public async Task An_incomplete_billing_profile_is_a_blocker_not_a_failure_on_preview()
    {
        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([nameof(SubscriptionBillingProfile.LegalName)]);

        var result = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ChargeMinor.Should().Be(1_000);

        var blocker = result.Value.Blockers.Should().ContainSingle().Which;
        blocker.Code.Should().Be("subscription_billing_profile_incomplete");
        blocker.Fields!["BillingProfile"].Should().Contain(nameof(SubscriptionBillingProfile.LegalName));
    }

    [Fact]
    public async Task An_incomplete_billing_profile_still_fails_the_real_change()
    {
        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([nameof(SubscriptionBillingProfile.LegalName)]);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_billing_profile_incomplete");
    }

    [Fact]
    public async Task No_saved_payment_method_is_a_blocker_only_when_an_upgrade_would_be_charged()
    {
        _account = new BillingAccount { ItemId = "acct-1", ProviderName = "STRIPE" };

        var result = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.Value!.ChargeMinor.Should().Be(1_000);
        result.Value.Blockers.Should().ContainSingle()
            .Which.Code.Should().Be("subscription_plan_change_no_payment_method");
    }

    [Fact]
    public async Task No_saved_payment_method_previews_cleanly_for_a_downgrade()
    {
        _account = new BillingAccount { ItemId = "acct-1", ProviderName = "STRIPE" };
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        var result = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        // Nothing is owed, so there is nothing a missing card would stop.
        result.Value!.ChargeMinor.Should().Be(0);
        result.Value.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_target_plan_fails_the_preview_exactly_as_it_fails_the_change()
    {
        var request = Request();
        request.PlanCode = "not-a-plan";

        var result = await Service().PreviewPlanChangeAsync(
            "sub-1", request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_plan_not_found");
    }

    /// <summary>
    /// Already the whole target period, tax included, undiminished by proration — pinned here so
    /// nothing quietly starts recomputing it.
    /// </summary>
    [Fact]
    public async Task NextRenewalAmountMinor_is_the_targets_own_full_period_total()
    {
        var result = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.Value!.NextRenewalAmountMinor.Should().Be(2_000);
    }

    // ---- Timing: what applies now and what waits for the paid period to end ------------------

    [Fact]
    public async Task An_upgrade_applies_immediately_and_schedules_nothing()
    {
        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scheduled.Should().BeNull();
    }

    [Fact]
    public async Task A_downgrade_is_scheduled_for_the_end_of_the_period_already_paid_for()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scheduled.Should().NotBeNull();
        _scheduled!.EffectiveAtUtc.Should().Be(_subscription.CurrentPeriodEndUtc);

        // Frozen, so a renewal a month later installs what was agreed rather than re-reading a
        // catalogue that may have moved.
        _scheduled.Plan.Code.Should().Be("premium");
        _scheduled.Price.UnitAmountMinor.Should().Be(1_000);
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A trial has paid for nothing, so it has no paid period to protect: its swap still applies
    /// at once, exactly as it did before scheduling existed.
    /// </summary>
    /// <remarks>
    /// The case the settlement rule alone would get wrong — a trial's settlement is always zero,
    /// which reads as "worth no more than what it replaces" and would schedule every trial swap.
    /// </remarks>
    [Fact]
    public async Task A_trial_swap_applies_immediately_even_though_it_settles_to_nothing()
    {
        _subscription = NewSubscription(SubscriptionStatus.Trialing, 2_000);
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scheduled.Should().BeNull();
    }

    /// <summary>
    /// An upgrade a credit balance covers in full is still an upgrade.
    /// </summary>
    /// <remarks>
    /// Classified on the settlement before credit pays for it. Reading the charge instead would
    /// schedule this for next month purely because the subscriber happened to hold a balance —
    /// they asked for more, and they get it now, paid out of the credit they already had.
    /// </remarks>
    [Fact]
    public async Task An_upgrade_covered_entirely_by_existing_credit_still_applies_immediately()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 1_000);
        _subscription.CreditBalanceMinor = 500_000;

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scheduled.Should().BeNull();
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the credit covered it, so there is nothing left to charge");
    }

    [Fact]
    public async Task A_preview_says_when_the_change_would_take_effect()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        var preview = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        preview.Value!.Timing.Should().Be("NextRenewal");
        preview.Value.EffectiveAtUtc.Should().Be(_subscription.CurrentPeriodEndUtc);
        preview.Value.ChargeMinor.Should().Be(0);
    }

    [Fact]
    public async Task An_upgrade_preview_says_it_would_apply_immediately()
    {
        var preview = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        preview.Value!.Timing.Should().Be("Immediate");
        preview.Value.ChargeMinor.Should().BeGreaterThan(0);
    }

    // ---- Opening stub: scheduled changes allowed, immediate ones still refused ----------------

    /// <summary>
    /// A downgrade inside a prepaid opening stub schedules for the end of the year that was paid
    /// for, not the end of the stub.
    /// </summary>
    /// <remarks>
    /// The blanket guard used to refuse this outright, before anything knew the change would take
    /// no money. Scheduling against <c>CurrentPeriodEndUtc</c> would be worse still: inside a
    /// prepaid stub that is the upcoming first of the month, so the subscriber would lose the plan
    /// about a month into a year they had settled in full.
    /// </remarks>
    [Fact]
    public async Task A_downgrade_inside_a_prepaid_opening_stub_schedules_for_the_paid_years_end()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _subscription.PendingAnnualPeriod = new PendingAnnualPeriod
        {
            StartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrepaid = true
        };
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scheduled!.EffectiveAtUtc.Should().Be(new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task An_upgrade_inside_a_prepaid_opening_stub_is_still_refused()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 1_000);
        _subscription.PendingAnnualPeriod = new PendingAnnualPeriod
        {
            StartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrepaid = true
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_initial_annual_period_prepaid");
    }

    [Fact]
    public async Task An_upgrade_inside_an_unpaid_opening_stub_says_the_year_is_unpaid()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 1_000);
        _subscription.PendingAnnualPeriod = new PendingAnnualPeriod
        {
            StartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrepaid = false
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_initial_annual_period_unpaid");
    }

    // ---- One pending commercial change at a time ----------------------------------------------

    [Fact]
    public async Task A_scheduled_quantity_change_blocks_a_plan_change()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _subscription.PendingQuantityChange = new PendingQuantityChange
        {
            RequestedQuantities = [],
            EffectiveAtUtc = _subscription.CurrentPeriodEndUtc
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_pending_quantity_change_exists");
    }

    [Fact]
    public async Task A_preview_is_never_blocked_by_a_scheduled_quantity_change()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _subscription.PendingQuantityChange = new PendingQuantityChange
        {
            RequestedQuantities = [],
            EffectiveAtUtc = _subscription.CurrentPeriodEndUtc
        };

        var preview = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        preview.IsSuccess.Should().BeTrue();
    }

    // ---- Cancelling a scheduled change ---------------------------------------------------------

    [Fact]
    public async Task Cancelling_a_scheduled_plan_change_clears_it()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _subscription.PendingPlanChange = new PendingPlanChange
        {
            Plan = new PlanSnapshot { Code = "premium" },
            EffectiveAtUtc = _subscription.CurrentPeriodEndUtc
        };
        _subscriptions
            .Setup(repository => repository.TryClearPendingPlanChangeAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Service().CancelPendingPlanChangeAsync(
            "sub-1", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PendingPlanChange.Should().BeNull();
    }

    [Fact]
    public async Task Cancelling_when_nothing_is_scheduled_reads_as_not_found()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);

        var result = await Service().CancelPendingPlanChangeAsync(
            "sub-1", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_pending_plan_change_not_found");
    }

    [Fact]
    public async Task Cancelling_against_a_moved_subscription_is_a_conflict()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _subscription.PendingPlanChange = new PendingPlanChange
        {
            Plan = new PlanSnapshot { Code = "premium" },
            EffectiveAtUtc = _subscription.CurrentPeriodEndUtc
        };
        _subscriptions
            .Setup(repository => repository.TryClearPendingPlanChangeAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().CancelPendingPlanChangeAsync(
            "sub-1", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_plan_change_conflict");
    }

    /// <summary>A scheduled change is replaced by a later one rather than queued behind it.</summary>
    [Fact]
    public async Task A_second_downgrade_replaces_the_one_already_scheduled()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _subscription.PendingPlanChange = new PendingPlanChange
        {
            Plan = new PlanSnapshot { Code = "somewhere-else" },
            EffectiveAtUtc = _subscription.CurrentPeriodEndUtc
        };
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scheduled!.Plan.Code.Should().Be("premium");
    }

    private SubscriptionPlanChangeService Service() => new(
        _contextResolver.Object,
        _subscriptions.Object,
        _catalogue.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new SubscriptionResponseMapper(),
        new ChangeSubscriptionPlanRequestValidator(),
        NullLogger<SubscriptionPlanChangeService>.Instance,
        _time,
        billingProfile: _billingProfile.Object);

    /// <summary>
    /// Only the allowance-snapshot test needs a real usage repository/resolver wired in — every
    /// other test above must keep exercising the legacy (no snapshot) path unchanged.
    /// </summary>
    private SubscriptionPlanChangeService ServiceWithUsage(ISubscriptionUsageRepository usage) => new(
        _contextResolver.Object,
        _subscriptions.Object,
        _catalogue.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new SubscriptionResponseMapper(),
        new ChangeSubscriptionPlanRequestValidator(),
        NullLogger<SubscriptionPlanChangeService>.Instance,
        _time,
        billingProfile: _billingProfile.Object,
        usage: usage,
        allowances: new MeterAllowanceResolver(usage));

    private static ChangeSubscriptionPlanRequest Request() => new()
    {
        PlanCode = "premium",
        PriceId = "price-2"
    };

    private static SubscriptionDetail NewSubscription(
        SubscriptionStatus status, long currentAmountMinor) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = status,
        CurrencyCode = "CHF",
        Version = 3,
        Plan = new PlanSnapshot { Code = "basic", DisplayName = "Basic" },
        Price = NewPriceSnapshot(currentAmountMinor),
        QuantityItems = [],
        CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static PriceSnapshot NewPriceSnapshot(long unitAmountMinor) => new()
    {
        CurrencyCode = "CHF",
        UnitAmountMinor = unitAmountMinor,
        Interval = BillingInterval.Month,
        IntervalCount = 1
    };

    private static Plan NewPlan() => new()
    {
        ItemId = "plan-2",
        TenantId = TenantId,
        Code = "premium",
        DisplayName = "Premium",
        Status = CatalogueStatus.Active
    };

    private static Price NewPrice(long unitAmountMinor, string currencyCode = "CHF") => new()
    {
        ItemId = "price-2",
        TenantId = TenantId,
        PlanId = "plan-2",
        CurrencyCode = currencyCode,
        UnitAmountMinor = unitAmountMinor,
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        Status = CatalogueStatus.Active
    };
}
