using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// What a calendar-aligned subscription is charged once it is past its opening stub.
/// </summary>
/// <remarks>
/// The stub is a property of the first period, not of the subscription. Every renewal after it
/// runs on a boundary and buys a whole month — with the single exception of a card-free trial,
/// which does its own first-period arithmetic at the moment it converts.
/// </remarks>
public sealed class CalendarAlignedRenewalTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string Zurich = "Europe/Zurich";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();

    /// <summary>1 September 2026, 00:00 Zurich — the renewal boundary itself.</summary>
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero));

    private SubscriptionTransition? _transition;
    private SubscriptionChargeRequest? _charge;
    private string? _idempotencyKey;

    public CalendarAlignedRenewalTests()
    {
        _billingAccounts
            .Setup(repository => repository.GetAsync(
                TenantId, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                TenantId = TenantId,
                OrganizationId = OrganizationId,
                ProviderName = "STRIPE",
                DefaultPaymentMethodId = "pm-1"
            });

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
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
                (request, key, _, _) =>
                {
                    _charge = request;
                    _idempotencyKey = key;
                })
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
    }

    [Fact]
    public async Task The_first_renewal_after_a_stub_charges_a_whole_month()
    {
        var subscription = CalendarSubscription(SubscriptionStatus.Active);

        // The stub it is closing: 25 August to 1 September.
        subscription.CurrentPeriodStartUtc = new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc);
        subscription.CurrentPeriodEndUtc = LocalMidnight(2026, 9, 1);
        subscription.InitialChargeProrated = true;
        subscription.ProrationDays = 7;
        subscription.ProrationTotalDays = 31;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(8_900,
            "the fraction belonged to the opening period, not to the subscription");
        _transition!.CurrentPeriodStartUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _transition.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 10, 1));
        _transition.NextFeeBillingAtUtc.Should().Be(LocalMidnight(2026, 10, 1));
    }

    /// <summary>
    /// A failed renewal and its retry have to land on the same boundary, or dunning bills the same
    /// month twice under two different keys.
    /// </summary>
    [Fact]
    public async Task A_retry_after_a_decline_charges_the_same_period_under_the_same_key()
    {
        var first = CalendarSubscription(SubscriptionStatus.Active);
        await Service().RenewAsync(first, CancellationToken.None);
        var firstKey = _idempotencyKey;

        // The same attempt, replayed after the charge was raised but before it was recorded.
        var retry = CalendarSubscription(SubscriptionStatus.Active);
        await Service().RenewAsync(retry, CancellationToken.None);

        _idempotencyKey.Should().Be(firstKey,
            "the key is derived from the period, which a retry does not move");
        _charge!.AmountMinor.Should().Be(8_900);
    }

    /// <summary>
    /// The trial case: nothing was charged at signup, so the first paid period runs from the day
    /// the trial ends to the next first, and is priced by those dates.
    /// </summary>
    [Fact]
    public async Task A_card_free_trial_ending_mid_month_charges_a_stub_to_the_next_first()
    {
        _time.Advance(
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        var subscription = CalendarSubscription(SubscriptionStatus.Trialing);
        subscription.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
            RequiresPaymentMethod = false
        };

        await Service().RenewAsync(subscription, CancellationToken.None);

        // 20 August through 31 August is 12 dates of 31: 8900 * 12 / 31 is 3445.16, so 3445.
        _charge!.AmountMinor.Should().Be(3_445);
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 1));
    }

    [Fact]
    public async Task A_card_free_trial_ending_on_the_first_starts_with_a_whole_month()
    {
        var subscription = CalendarSubscription(SubscriptionStatus.Trialing);
        subscription.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = LocalMidnight(2026, 9, 1),
            RequiresPaymentMethod = false
        };

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(8_900, "the trial ended exactly on a boundary");
    }

    /// <summary>
    /// A worker that picks the conversion up late must still charge for the days the subscriber
    /// was entitled to — pricing from the clock would shorten the period by however late it ran.
    /// </summary>
    [Fact]
    public async Task A_late_trial_conversion_is_still_priced_from_the_trial_end_date()
    {
        _time.Advance(
            new DateTimeOffset(2026, 8, 23, 3, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        var subscription = CalendarSubscription(SubscriptionStatus.Trialing);
        subscription.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
            RequiresPaymentMethod = false
        };

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(3_445,
            "the subscriber was entitled from the 20th whatever day the sweep ran");
    }

    [Fact]
    public async Task An_anniversary_renewal_is_untouched_by_any_of_this()
    {
        var subscription = CalendarSubscription(SubscriptionStatus.Active);
        subscription.Price.BillingAlignment = BillingAlignment.Anniversary;
        subscription.FeeSchedule.AnchorDayOfMonth = 25;
        subscription.FeeSchedule.AnchorInstantUtc =
            new DateTime(2026, 7, 25, 9, 30, 0, DateTimeKind.Utc);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(8_900);

        // Still the 25th in the subscriber's own calendar, which is the day they bought on.
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 25));
    }

    private SubscriptionRenewalService Service() => new(
        _subscriptions.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new OptionsStub(),
        NullLogger<SubscriptionRenewalService>.Instance,
        _time);

    private static DateTime LocalMidnight(int year, int month, int day) =>
        BillingLocalTime.ToUtc(
            new DateTime(year, month, day, 0, 0, 0),
            TimeZoneInfo.FindSystemTimeZoneById(Zurich));

    private static SubscriptionDetail CalendarSubscription(SubscriptionStatus status) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = status,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
        Price = new PriceSnapshot
        {
            CurrencyCode = "CHF",
            UnitAmountMinor = 8_900,
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            BillingAlignment = BillingAlignment.CalendarMonth
        },
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = LocalMidnight(2026, 8, 1),
            TimeZoneId = Zurich,
            AnchorDayOfMonth = 1,
            AnchorMinutesFromMidnight = 0
        }
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new() { DunningMaxAttempts = 4 };

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
