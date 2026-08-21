using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
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

    private SubscriptionRenewalService Service() => new(
        _subscriptions.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new OptionsStub(),
        NullLogger<SubscriptionRenewalService>.Instance,
        _time);

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

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new() { DunningMaxAttempts = 4 };

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
