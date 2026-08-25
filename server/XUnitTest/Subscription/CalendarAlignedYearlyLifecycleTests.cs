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
/// A calendar-aligned yearly subscription after signup: converting from a trial, surviving a
/// decline, and being moved onto from a monthly plan.
/// </summary>
/// <remarks>
/// Each of these has to produce the same two charges a fresh mid-month signup does — a stub priced
/// from the monthly basis, then a whole year opening on the first — reached by a different route.
/// Tier 2 throughout: CHF 950 a month, 8% off annually.
/// </remarks>
public sealed class CalendarAlignedYearlyLifecycleTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string Zurich = "Europe/Zurich";
    private const long MonthlyMinor = 95_000;
    private const long YearlyGrossMinor = MonthlyMinor * 12;

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();

    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionTransition? _transition;
    private SubscriptionChargeRequest? _charge;
    private string? _idempotencyKey;
    private bool _declineCharge;

    public CalendarAlignedYearlyLifecycleTests()
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
            .ReturnsAsync(() => _declineCharge
                ? SubscriptionOperationResult<string>.Failure(
                    PaymentFailureKind.ProviderRejected, "card_declined", "Declined.", "corr-1")
                : SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
    }

    /// <summary>
    /// A trial ending mid-month buys the rest of that month at the monthly price, and only then
    /// starts paying for a year.
    /// </summary>
    [Fact]
    public async Task A_card_free_trial_ending_mid_month_charges_the_monthly_stub()
    {
        var subscription = ConvertingTrial();

        await Service().RenewAsync(subscription, CancellationToken.None);

        // 95000 x 12/31 is 36774; less 8% (2941) is 33833.
        _charge!.AmountMinor.Should().Be(33_833);
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _transition.NextFeeBillingAtUtc.Should().Be(LocalMidnight(2026, 9, 1),
            "the year itself is still due, and is charged separately on the first");
    }

    [Fact]
    public async Task The_year_after_the_conversion_stub_is_charged_in_full_and_separately()
    {
        var subscription = ConvertingTrial();
        await Service().RenewAsync(subscription, CancellationToken.None);
        var stubKey = _idempotencyKey;

        // The subscription as the conversion left it, now on the first.
        subscription.Status = SubscriptionStatus.Active;
        subscription.InitialChargeAmountMinor = _transition!.InitialChargeAmountMinor;
        subscription.CurrentPeriodStartUtc = _transition.CurrentPeriodStartUtc!.Value;
        subscription.CurrentPeriodEndUtc = _transition.CurrentPeriodEndUtc!.Value;
        _time.Advance(LocalMidnight(2026, 9, 1) - _time.GetUtcNow());

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(1_048_800, "a whole discounted year");
        _idempotencyKey.Should().NotBe(stubKey,
            "the stub and the year are different periods and must not collide on one key");
        _transition!.CurrentPeriodStartUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _transition.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2027, 9, 1));
    }

    /// <summary>
    /// The dunning guarantee established for monthly stubs, on a yearly one: a declined conversion
    /// is no longer <c>Trialing</c>, and the retry must still owe the stub.
    /// </summary>
    [Fact]
    public async Task A_declined_yearly_conversion_still_retries_the_monthly_stub()
    {
        var subscription = ConvertingTrial();

        _declineCharge = true;
        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.PastDue);

        subscription.Status = SubscriptionStatus.PastDue;
        subscription.DunningAttemptCount = 1;
        _declineCharge = false;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(33_833,
            "still a fraction of the month, not a fraction of the year and not a whole year");
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 1));
    }

    /// <summary>
    /// Moving from a monthly plan onto a calendar-aligned yearly one settles only the target stub
    /// now; the annual cycle opens on the first like any other calendar-aligned year.
    /// </summary>
    [Fact]
    public void A_monthly_to_yearly_change_settles_only_the_target_stub()
    {
        var now = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

        var subscription = new SubscriptionDetail
        {
            ItemId = "sub-1",
            CurrencyCode = "CHF",
            Plan = new PlanSnapshot { Code = "tier-2", DisplayName = "Tier 2" },
            Price = new PriceSnapshot
            {
                CurrencyCode = "CHF",
                UnitAmountMinor = MonthlyMinor,
                Interval = BillingInterval.Month,
                IntervalCount = 1
            },
            // A whole month from 1 August, so seven days of it are unused on the 25th.
            CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription,
            new PlanSnapshot { Code = "tier-2", DisplayName = "Tier 2" },
            YearlySnapshot(),
            [],
            now,
            now,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new BillingDayFraction(7, 31));

        // Arriving: the same 19736 stub a fresh signup pays. Leaving: 95000 x 7/31 of the monthly
        // plan is 21451 unused. The difference is banked rather than charged, because the annual
        // discount makes the stub cheaper than the month it replaces.
        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(1_715);
    }

    private SubscriptionDetail ConvertingTrial()
    {
        var subscription = YearlySubscription(SubscriptionStatus.Trialing);
        subscription.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
            RequiresPaymentMethod = false
        };

        return subscription;
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

    private static PriceSnapshot YearlySnapshot() => new()
    {
        PriceId = "price-yearly",
        CurrencyCode = "CHF",
        UnitAmountMinor = YearlyGrossMinor,
        Interval = BillingInterval.Year,
        IntervalCount = 1,
        BillingAlignment = BillingAlignment.CalendarMonth,
        CalendarStubBasePriceId = "price-monthly",
        CalendarStubBaseUnitAmountMinor = MonthlyMinor,
        AutomaticDiscountBasisPoints = 800
    };

    private static SubscriptionDetail YearlySubscription(SubscriptionStatus status) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = status,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot { Code = "tier-2", DisplayName = "Tier 2" },
        Price = YearlySnapshot(),
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Year,
            IntervalCount = 1,
            AnchorInstantUtc = LocalMidnight(2026, 9, 1),
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
