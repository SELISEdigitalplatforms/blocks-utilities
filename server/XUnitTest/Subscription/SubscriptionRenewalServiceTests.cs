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
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
    }

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
