using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Changing how many units a subscription has bought: charged now going up, scheduled going down.
/// </summary>
/// <remarks>
/// The bands under test are CHF 145 per user per month, discounted 0/5/10/15/20% at 1/5/10/20/30
/// users. The clock sits exactly halfway through a 31-day August period, so a prorated figure is
/// half the full-period difference.
/// </remarks>
public sealed class SubscriptionQuantityChangeServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const long UnitAmount = 14_500;

    private static readonly DateTime PeriodStart = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

    private readonly List<string> _calls = [];

    private SubscriptionDetail _subscription = NewSubscription(4);
    private SettlementReservation? _reservation;
    private BillingAccount? _account = new()
    {
        ItemId = "acct-1",
        ProviderName = "STRIPE",
        DefaultPaymentMethodId = "pm-1",
        ProviderCustomerId = "cus_123"
    };

    public SubscriptionQuantityChangeServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _subscriptions
            .Setup(repository => repository.TryApplyQuantityChangeAsync(
                TenantId, "sub-1", It.IsAny<int>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()))
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryReserveSettlementAsync(
                TenantId, "sub-1", It.IsAny<int>(),
                It.IsAny<SettlementReservation>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, int _, SettlementReservation taken, CancellationToken _) =>
            {
                _reservation = taken;
                _calls.Add("reserve");
            })
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryPromoteQuantityReservationAsync(
                TenantId, "sub-1", It.IsAny<string>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("promote"))
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryReleaseSettlementAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("release"))
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TrySetPendingQuantityChangeAsync(
                TenantId, "sub-1", It.IsAny<int>(),
                It.IsAny<PendingQuantityChange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryClearPendingQuantityChangeAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _billingAccounts
            .Setup(repository => repository.GetAsync(
                TenantId, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _account);

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("charge"))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
    }

    [Theory]
    // The plan's boundary table: crossing upward is charged now, downward waits for renewal.
    [InlineData(4, 5, "Immediate", 0, 500)]
    [InlineData(5, 4, "NextPeriod", 500, 0)]
    [InlineData(9, 10, "Immediate", 500, 1_000)]
    [InlineData(10, 9, "NextPeriod", 1_000, 500)]
    [InlineData(19, 20, "Immediate", 1_000, 1_500)]
    [InlineData(20, 19, "NextPeriod", 1_500, 1_000)]
    [InlineData(29, 30, "Immediate", 1_500, 2_000)]
    [InlineData(30, 29, "NextPeriod", 2_000, 1_500)]
    public async Task Crossing_a_band_moves_the_discount_and_decides_the_timing(
        long from,
        long to,
        string expectedTiming,
        int expectedCurrentBasisPoints,
        int expectedTargetBasisPoints)
    {
        _subscription = NewSubscription(from);

        var result = await Service().ChangeAsync("sub-1", Request(to), "corr-1", default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Timing.Should().Be(expectedTiming);
        result.Value.CurrentTier!.DiscountBasisPoints.Should().Be(expectedCurrentBasisPoints);
        result.Value.TargetTier!.DiscountBasisPoints.Should().Be(expectedTargetBasisPoints);
    }

    [Fact]
    public async Task An_increase_charges_the_prorated_difference_between_discounted_totals()
    {
        // 4 users at 0% is CHF 580.00; 5 at 5% is CHF 688.75. The difference is CHF 108.75, and
        // the clock sits halfway through the period, so about CHF 54.37 is due now.
        var result = await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        result.Value!.ProratedChargeMinor.Should().BeCloseTo(5_437, 2);
        result.Value.NextRenewalAmountMinor.Should().Be(68_875);
    }

    [Fact]
    public async Task The_quote_states_the_unit_price_rather_than_leaving_it_to_be_derived()
    {
        var result = await Service().PreviewAsync("sub-1", Request(5), "corr-1", default);

        // CHF 688.75 for five users, so CHF 137.75 each. Taken from the period total rather than
        // recomputed, so the two figures on a confirmation screen cannot disagree.
        result.Value!.NextRenewalAmountMinor.Should().Be(68_875);
        result.Value.EffectiveUnitAmountMinor.Should().Be(13_775);
        result.Value.PromotionApplied.Should().BeFalse();
    }

    [Fact]
    public async Task The_unit_price_reflects_a_promotion_the_band_arithmetic_knows_nothing_about()
    {
        // The reason a client must not compute this. A percentage applied to the list price gives
        // CHF 137.75 a unit; the plan's BestDiscount policy takes the larger reduction instead, so
        // the real figure is CHF 116.00 — and a screen showing 137.75 would be quoting a number
        // nobody is about to be charged.
        _subscription.Discount = new DiscountTerms
        {
            Code = "LAUNCH20",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_000
        };

        var result = await Service().PreviewAsync("sub-1", Request(5), "corr-1", default);

        result.Value!.NextRenewalAmountMinor.Should().Be(58_000);
        result.Value.EffectiveUnitAmountMinor.Should().Be(11_600);
        result.Value.PromotionApplied.Should().BeTrue();
    }

    [Fact]
    public async Task A_flat_fee_price_states_no_unit_price_at_all()
    {
        // Nothing is sold by the unit here. Reporting the plan's whole fee would have a client
        // print it as the cost of each of something the plan merely tracks for free.
        _subscription.Price.QuantityItemKey = string.Empty;

        var result = await Service().PreviewAsync("sub-1", Request(5), "corr-1", default);

        result.Value!.EffectiveUnitAmountMinor.Should().BeNull();
    }

    [Fact]
    public async Task The_unit_price_is_stated_before_tax_and_the_tax_beside_it()
    {
        // Divided out of the tax-inclusive total, a 5% band on an 8% taxed price reported CHF
        // 148.77 a unit — more than the undiscounted CHF 145 list price, on a card that also said
        // the band took 5% off.
        _subscription.Price.TaxRateBasisPoints = 800;

        var result = await Service().PreviewAsync("sub-1", Request(5), "corr-1", default);

        result.Value!.EffectiveUnitAmountMinor.Should().Be(13_775);
        result.Value.TaxAmountMinor.Should().Be(5_510);
        result.Value.NextRenewalAmountMinor.Should().Be(74_385, "the total is tax-inclusive");
    }

    [Fact]
    public async Task An_untaxed_price_states_no_tax()
    {
        var result = await Service().PreviewAsync("sub-1", Request(5), "corr-1", default);

        result.Value!.TaxAmountMinor.Should().Be(0);
    }

    [Fact]
    public async Task An_increase_is_reserved_then_charged_then_granted()
    {
        await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        _calls.Should().Equal(
            "reserve",
            "charge",
            "promote");

        _subscriptions.Verify(
            repository => repository.TryPromoteQuantityReservationAsync(
                TenantId, "sub-1", _reservation!.ReservationId,
                It.Is<List<SubscriptionQuantityItem>>(items => items.Single().Quantity == 5),
                It.IsAny<long>(), "pay-1", It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_paid_increase_is_charged_under_its_reservation_rather_than_its_version()
    {
        string? key = null;
        string? orderId = null;

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((SubscriptionChargeRequest request, string k, string _, CancellationToken _) =>
            {
                key = k;
                orderId = request.OrderId;
            })
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        // Keyed on the reservation, which nothing else can move. Keyed on the version, a retry
        // after a concurrent change would build a different key and charge a second time.
        key.Should().Be(SubscriptionConstants.SettlementChargeKeyFor("sub-1", _reservation!.ReservationId));
        orderId.Should().Be(
            SubscriptionConstants.SettlementOrderIdFor(
                "sub-1", SettlementReservationKind.QuantityIncrease, _reservation.ReservationId));
        orderId.Should().Contain(
            $":{SubscriptionConstants.QuantitySegment}:",
            "invoice history reads the kind back out of the order id");
    }

    [Fact]
    public async Task A_settled_charge_grants_the_units_even_though_the_version_has_moved()
    {
        // The whole point of the reservation. Between the reservation and the charge settling,
        // something else bumps the version; the promotion is addressed by the reservation, so the
        // units the subscriber has just paid for are still granted.
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                _calls.Add("charge");
                _subscription.Version += 1;
            })
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        var result = await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        result.IsSuccess.Should().BeTrue(
            "money moved, so the units must be granted rather than reported as a conflict");
        _calls.Should().Contain("promote");
        _subscriptions.Verify(
            repository => repository.TryApplyQuantityChangeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()),
            Times.Never,
            "a paid increase must never be granted by a version-keyed write");
    }

    [Fact]
    public async Task A_second_change_while_one_is_settling_is_refused()
    {
        _subscription.SettlementReservation = new SettlementReservation
        {
            ReservationId = "reservation-1",
            Kind = SettlementReservationKind.QuantityIncrease,
            QuantityChange = new ReservedQuantityChange { RequestedQuantities = [Item(5)] },
            ChargeAmountMinor = 5_437,
            ReservedAtUtc = PeriodStart
        };

        var result = await Service().ChangeAsync("sub-1", Request(6), "corr-1", default);

        result.ErrorCode.Should().Be("subscription_quantity_change_in_flight");
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_declined_increase_leaves_the_quantity_untouched()
    {
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("charge"))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "Declined.", "corr-1"));

        var result = await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        result.IsSuccess.Should().BeFalse();

        // One code for a declined increase, whatever word the acquirer used.
        result.ErrorCode.Should().Be("subscription_quantity_charge_failed");
        _calls.Should().Equal("reserve", "charge", "release");

        _subscriptions.Verify(
            repository => repository.TryPromoteQuantityReservationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "seats must never be granted by a charge that failed");
    }

    [Theory]
    [InlineData(PaymentFailureKind.Timeout)]
    [InlineData(PaymentFailureKind.Unavailable)]
    [InlineData(PaymentFailureKind.ProviderFailure)]
    [InlineData(PaymentFailureKind.Unexpected)]
    public async Task An_unanswered_charge_keeps_its_reservation_rather_than_inviting_a_retry(
        PaymentFailureKind kind)
    {
        // Not a decline: the provider may have collected and lost the reply. Releasing here would
        // let the next attempt open a fresh reservation, charge under a new key, and take the money
        // twice — so the reservation stands and reconciliation settles it.
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("charge"))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                kind, "provider_unreachable", "No answer.", "corr-1"));

        var result = await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        result.ErrorCode.Should().Be("subscription_quantity_charge_unresolved");

        // The provider's own kind, so the caller sees 502, 503 or 504 rather than a decline it can
        // retry straight into a second charge.
        result.FailureKind.Should().Be(kind);
        _calls.Should().Equal("reserve", "charge");
        _subscriptions.Verify(
            repository => repository.TryReleaseSettlementAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "an unanswered charge may have been collected");
    }

    [Fact]
    public async Task An_increase_with_no_saved_card_is_refused_before_the_gateway_is_called()
    {
        _account = new BillingAccount { ItemId = "acct-1", ProviderName = "STRIPE" };

        var result = await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        result.ErrorCode.Should().Be("subscription_payment_method_missing");
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_decrease_is_scheduled_for_the_period_end_and_never_charged()
    {
        _subscription = NewSubscription(10);

        var result = await Service().ChangeAsync("sub-1", Request(9), "corr-1", default);

        result.Value!.Timing.Should().Be("NextPeriod");
        result.Value.ProratedChargeMinor.Should().Be(0, "a decrease is never refunded or charged");
        result.Value.EffectiveAtUtc.Should().Be(PeriodEnd);
        result.Value.PendingQuantityChange!.Quantities.Single().Quantity.Should().Be(9);

        _gateway.VerifyNoOtherCalls();
        _subscriptions.Verify(
            repository => repository.TrySetPendingQuantityChangeAsync(
                TenantId, "sub-1", 7, It.IsAny<PendingQuantityChange>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_decrease_quotes_the_next_renewal_at_the_smaller_quantity_and_its_band()
    {
        _subscription = NewSubscription(10);

        var result = await Service().ChangeAsync("sub-1", Request(9), "corr-1", default);

        // 9 users at 5%: CHF 145 x 9 x 0.95 = CHF 1,239.75.
        result.Value!.NextRenewalAmountMinor.Should().Be(123_975);
    }

    [Fact]
    public async Task A_free_item_rising_cannot_turn_a_reduction_into_an_immediate_one()
    {
        // Only the item the price is written against carries money, so summing every item lets a
        // free one outvote a priced one. Summed, 10 users + 1 project to 8 + 5 looks like a rise;
        // what the subscriber asked for is two fewer users, which they have already paid for.
        _subscription = NewSubscriptionWithFreeItem(users: 10, projects: 1);

        var result = await Service().ChangeAsync(
            "sub-1",
            Request(("user", 8), ("project", 5)),
            "corr-1",
            default);

        result.Value!.Timing.Should().Be("NextPeriod");
        result.Value.PendingQuantityChange.Should().NotBeNull();
        _gateway.VerifyNoOtherCalls();
        _calls.Should().BeEmpty("nothing is reserved or charged for a reduction");
    }

    [Fact]
    public async Task A_change_that_moves_only_free_items_applies_at_once_without_a_charge()
    {
        _subscription = NewSubscriptionWithFreeItem(users: 10, projects: 1);

        var result = await Service().ChangeAsync(
            "sub-1",
            Request(("project", 5)),
            "corr-1",
            default);

        result.Value!.Timing.Should().Be("Immediate");
        result.Value.ProratedChargeMinor.Should().Be(0);
        _gateway.VerifyNoOtherCalls();
        _subscriptions.Verify(
            repository => repository.TryApplyQuantityChangeAsync(
                TenantId, "sub-1", 7, It.IsAny<List<SubscriptionQuantityItem>>(),
                It.IsAny<long>(), null, It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "nothing is owed, so there is nothing to reserve against");
    }

    [Fact]
    public async Task A_preview_calculates_the_same_figures_and_writes_nothing()
    {
        var preview = await Service().PreviewAsync("sub-1", Request(5), "corr-1", default);
        var applied = await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        preview.Value!.Preview.Should().BeTrue();
        preview.Value.ProratedChargeMinor.Should().Be(applied.Value!.ProratedChargeMinor);
        preview.Value.NextRenewalAmountMinor.Should().Be(applied.Value.NextRenewalAmountMinor);

        _calls.Should().Equal(
            ["reserve", "charge", "promote"],
            "only the apply may write, and it ran once");
    }

    [Fact]
    public async Task A_stale_version_is_refused_before_anything_is_priced()
    {
        var result = await Service().ChangeAsync(
            "sub-1",
            new ChangeQuantityRequest
            {
                Version = 3,
                Quantities = [new QuantityChangeItemRequest { ItemKey = "user", Quantity = 5 }]
            },
            "corr-1",
            default);

        result.ErrorCode.Should().Be("subscription_version_conflict");
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_lost_compare_and_set_is_reported_as_a_conflict_before_anything_is_spent()
    {
        _subscriptions
            .Setup(repository => repository.TryReserveSettlementAsync(
                TenantId, "sub-1", It.IsAny<int>(),
                It.IsAny<SettlementReservation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        result.ErrorCode.Should().Be("subscription_version_conflict");
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_same_quantity_is_refused_rather_than_charged_nothing()
    {
        var result = await Service().ChangeAsync("sub-1", Request(4), "corr-1", default);

        result.ErrorCode.Should().Be("subscription_quantity_unchanged");
    }

    [Fact]
    public async Task A_quantity_outside_the_snapshotted_bounds_is_refused()
    {
        _subscription.Plan.QuantityItems[0].MaxQuantity = 20;

        var result = await Service().ChangeAsync("sub-1", Request(21), "corr-1", default);

        result.ErrorCode.Should().Be("subscription_quantity_invalid");
    }

    [Fact]
    public async Task An_item_the_plan_does_not_define_is_refused()
    {
        var result = await Service().ChangeAsync(
            "sub-1",
            new ChangeQuantityRequest
            {
                Version = 7,
                Quantities = [new QuantityChangeItemRequest { ItemKey = "workspace", Quantity = 5 }]
            },
            "corr-1",
            default);

        result.ErrorCode.Should().Be("subscription_quantity_item_unknown");
    }

    [Fact]
    public async Task A_cancelled_subscription_cannot_change_quantity()
    {
        _subscription.Status = SubscriptionStatus.Canceled;

        var result = await Service().ChangeAsync("sub-1", Request(5), "corr-1", default);

        result.ErrorCode.Should().Be("subscription_quantity_change_not_allowed");
    }

    [Fact]
    public async Task A_scheduled_decrease_can_be_withdrawn()
    {
        _subscription.PendingQuantityChange = new PendingQuantityChange
        {
            RequestedQuantities = [Item(3)],
            EffectiveAtUtc = PeriodEnd
        };

        var result = await Service().CancelPendingAsync("sub-1", null, "corr-1", default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PendingQuantityChange.Should().BeNull();
        _subscriptions.Verify(
            repository => repository.TryClearPendingQuantityChangeAsync(
                TenantId, "sub-1", 7, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Withdrawing_a_decrease_that_was_never_scheduled_says_so()
    {
        var result = await Service().CancelPendingAsync("sub-1", null, "corr-1", default);

        result.ErrorCode.Should().Be("subscription_pending_quantity_change_not_found");
    }

    [Fact]
    public async Task A_reservation_announces_its_own_recovery_before_the_charge_is_raised()
    {
        // Announced before the money moves, so a reservation stranded by a dying process is already
        // known about rather than waiting to be discovered by a roster pass.
        var scheduler = new Mock<ISubscriptionWorkScheduler>();
        var order = new List<string>();

        scheduler
            .Setup(candidate => candidate.ScheduleReservationRecoveryAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<SettlementReservation>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("schedule"))
            .Returns(Task.CompletedTask);

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("charge"))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        await Service(scheduler.Object).ChangeAsync("sub-1", Request(5), "corr-1", default);

        order.Should().Equal("schedule", "charge");
        scheduler.Verify(
            candidate => candidate.ScheduleReservationRecoveryAsync(
                It.Is<SubscriptionDetail>(subscription => subscription.ItemId == "sub-1"),
                It.Is<SettlementReservation>(reservation =>
                    reservation.ReservationId == _reservation!.ReservationId),
                "corr-1",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_increase_with_nothing_to_charge_announces_no_recovery()
    {
        // No reservation is taken when nothing is owed, so there is nothing to recover.
        var scheduler = new Mock<ISubscriptionWorkScheduler>();
        _subscription = NewSubscriptionWithFreeItem(users: 10, projects: 1);

        await Service(scheduler.Object).ChangeAsync(
            "sub-1", Request(("project", 5)), "corr-1", default);

        scheduler.Verify(candidate => candidate.ScheduleReservationRecoveryAsync(
            It.IsAny<SubscriptionDetail>(),
            It.IsAny<SettlementReservation>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        scheduler.Verify(candidate => candidate.ScheduleOutboxPublicationAsync(
            It.Is<SubscriptionDetail>(subscription => subscription.ItemId == "sub-1"),
            It.IsAny<SubscriptionOutboxEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private SubscriptionQuantityChangeService Service(
        ISubscriptionWorkScheduler? scheduler = null) => new(
        _contextResolver.Object,
        _subscriptions.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new ChangeQuantityRequestValidator(),
        NullLogger<SubscriptionQuantityChangeService>.Instance,
        _time,
        scheduler);

    private static ChangeQuantityRequest Request(long quantity) => new()
    {
        Version = 7,
        Quantities = [new QuantityChangeItemRequest { ItemKey = "user", Quantity = quantity }]
    };

    private static ChangeQuantityRequest Request(params (string ItemKey, long Quantity)[] items) =>
        new()
        {
            Version = 7,
            Quantities = items
                .Select(item => new QuantityChangeItemRequest
                {
                    ItemKey = item.ItemKey,
                    Quantity = item.Quantity
                })
                .ToList()
        };

    /// <summary>The priced item alongside one the price is not written against, so costs nothing.</summary>
    private static SubscriptionDetail NewSubscriptionWithFreeItem(long users, long projects)
    {
        var subscription = NewSubscription(users);

        subscription.QuantityItems.Add(new SubscriptionQuantityItem
        {
            ItemKey = "project",
            UnitLabel = "project",
            Quantity = projects,
            UnitAmountMinor = 0
        });

        subscription.Plan.QuantityItems.Add(new PlanQuantityItem
        {
            ItemKey = "project",
            UnitLabel = "project",
            MinQuantity = 1
        });

        return subscription;
    }

    private static SubscriptionQuantityItem Item(long quantity) => new()
    {
        ItemKey = "user",
        UnitLabel = "user",
        Quantity = quantity,
        UnitAmountMinor = UnitAmount
    };

    private static SubscriptionDetail NewSubscription(long quantity) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Active,
        Version = 7,
        CurrencyCode = "CHF",
        CurrentPeriodStartUtc = PeriodStart,
        CurrentPeriodEndUtc = PeriodEnd,
        QuantityItems = [Item(quantity)],
        Price = new PriceSnapshot
        {
            UnitAmountMinor = UnitAmount,
            CurrencyCode = "CHF",
            QuantityItemKey = "user",
            Interval = BillingInterval.Month,
            IntervalCount = 1
        },
        Plan = new PlanSnapshot
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
                        new QuantityDiscountTier { MinimumQuantity = 5, MaximumQuantity = 9, DiscountBasisPoints = 500 },
                        new QuantityDiscountTier { MinimumQuantity = 10, MaximumQuantity = 19, DiscountBasisPoints = 1_000 },
                        new QuantityDiscountTier { MinimumQuantity = 20, MaximumQuantity = 29, DiscountBasisPoints = 1_500 },
                        new QuantityDiscountTier { MinimumQuantity = 30, MaximumQuantity = null, DiscountBasisPoints = 2_000 }
                    ]
                }
            ]
        }
    };
}
