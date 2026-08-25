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
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = NewSubscription(SubscriptionStatus.Active, 1_000);
    private SettlementReservation? _reserved;
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
                It.IsAny<CancellationToken>()))
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
                It.IsAny<CancellationToken>()),
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
        // A downgrade moves no money, so there is nothing to settle and nothing to lock.
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);

        await Service().ChangePlanAsync("sub-1", Request(), "corr-1", CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.TryReserveSettlementAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<SettlementReservation>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                TenantId,
                "sub-1",
                It.IsAny<int>(),
                null,
                It.IsAny<PlanSnapshot>(),
                It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(),
                It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(),
                It.IsAny<long>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "with no reservation, the version is what addresses the write");
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
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
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
        _time);

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
