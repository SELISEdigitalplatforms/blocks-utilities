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
/// What happens on 1 September, when the year a subscriber bought in August actually begins.
/// </summary>
/// <remarks>
/// Two outcomes from one code path: an unpaid year is collected here, a prepaid one is simply
/// opened. Both end with the subscription inside the year and nothing left pending — the difference
/// is only whether money moves.
/// </remarks>
public sealed class CalendarAnnualBoundaryTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string Zurich = "Europe/Zurich";
    private const long DiscountedAnnualMinor = 1_048_800;

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();

    /// <summary>1 September 2026, 00:00 in Zurich — the boundary itself.</summary>
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero));

    private SubscriptionTransition? _transition;
    private SubscriptionChargeRequest? _charge;
    private int _chargeCount;
    private bool _declineCharge;

    public CalendarAnnualBoundaryTests()
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
                (request, _, _, _) =>
                {
                    _charge = request;
                    _chargeCount++;
                })
            .ReturnsAsync(() => _declineCharge
                ? SubscriptionOperationResult<string>.Failure(
                    PaymentFailureKind.ProviderRejected, "card_declined", "Declined.", "corr-1")
                : SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
    }

    [Fact]
    public async Task An_unpaid_year_is_collected_at_its_boundary()
    {
        var subscription = InStub(prepaid: false);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(DiscountedAnnualMinor);
        _transition!.CurrentPeriodStartUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _transition.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2027, 9, 1));
        _transition.NextFeeBillingAtUtc.Should().Be(LocalMidnight(2027, 9, 1));
        _transition.ClearPendingAnnualPeriod.Should().BeTrue();
    }

    /// <summary>
    /// The one that would double-charge if the prepaid flag were ignored.
    /// </summary>
    [Fact]
    public async Task A_prepaid_year_opens_without_taking_anything()
    {
        var subscription = InStub(prepaid: true);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _chargeCount.Should().Be(0, "the money came in with the opening charge a month ago");
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Active);
        _transition.CurrentPeriodStartUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _transition.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2027, 9, 1));
        _transition.ClearPendingAnnualPeriod.Should().BeTrue();
    }

    /// <summary>
    /// The frozen figure, not one derived at the boundary. A month has passed since the quote and
    /// nothing about the catalogue is guaranteed to have stood still.
    /// </summary>
    [Fact]
    public async Task The_boundary_charges_what_was_quoted_not_what_the_price_now_says()
    {
        var subscription = InStub(prepaid: false);
        subscription.PendingAnnualPeriod!.AmountMinor = 999_999;
        subscription.Price.UnitAmountMinor = 5_000_000;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(999_999);
    }

    [Fact]
    public async Task A_declined_annual_charge_enters_dunning_and_keeps_the_year_pending()
    {
        var subscription = InStub(prepaid: false);
        _declineCharge = true;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.PastDue);
        _transition.ClearPendingAnnualPeriod.Should().BeFalse(
            "the year is still owed, so it must still be found on the retry");
    }

    [Fact]
    public async Task A_dunning_retry_charges_the_same_frozen_year()
    {
        var subscription = InStub(prepaid: false);
        _declineCharge = true;
        await Service().RenewAsync(subscription, CancellationToken.None);

        subscription.Status = SubscriptionStatus.PastDue;
        subscription.DunningAttemptCount = 1;
        _declineCharge = false;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(DiscountedAnnualMinor);
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2027, 9, 1));
        _transition.ClearPendingAnnualPeriod.Should().BeTrue();
    }

    /// <summary>
    /// The year that follows is an ordinary renewal, priced from the price rather than from a
    /// record that no longer exists.
    /// </summary>
    [Fact]
    public async Task The_second_year_renews_normally()
    {
        var subscription = InStub(prepaid: true);
        subscription.PendingAnnualPeriod = null;
        subscription.CurrentPeriodStartUtc = LocalMidnight(2026, 9, 1);
        subscription.CurrentPeriodEndUtc = LocalMidnight(2027, 9, 1);
        _time.Advance(LocalMidnight(2027, 9, 1) - _time.GetUtcNow());

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(DiscountedAnnualMinor);
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2028, 9, 1));
    }

    /// <summary>
    /// A subscription still inside its stub has nothing to open — the boundary has not arrived, and
    /// the renewal running is the one closing the stub itself.
    /// </summary>
    [Fact]
    public async Task A_year_whose_boundary_has_not_arrived_is_left_alone()
    {
        var subscription = InStub(prepaid: false);
        subscription.PendingAnnualPeriod!.StartUtc = LocalMidnight(2026, 10, 1);
        subscription.PendingAnnualPeriod.EndUtc = LocalMidnight(2027, 10, 1);

        // A figure no ordinary calculation could produce, so the charge below can only match it by
        // having been taken from the pending record.
        subscription.PendingAnnualPeriod.AmountMinor = 999_999;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.ClearPendingAnnualPeriod.Should().BeFalse();
        _charge!.AmountMinor.Should().NotBe(999_999,
            "the year is not due yet, so this renewal must price itself from the price");
        _charge.AmountMinor.Should().Be(DiscountedAnnualMinor);
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

    /// <summary>A subscription that signed up 25 August and is inside its stub.</summary>
    private static SubscriptionDetail InStub(bool prepaid) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot { Code = "tier-2", DisplayName = "Tier 2" },
        Price = new PriceSnapshot
        {
            PriceId = "price-yearly",
            CurrencyCode = "CHF",
            UnitAmountMinor = 1_140_000,
            Interval = BillingInterval.Year,
            IntervalCount = 1,
            BillingAlignment = BillingAlignment.CalendarMonth,
            CalendarStubBasePriceId = "price-monthly",
            CalendarStubBaseUnitAmountMinor = 95_000,
            CalendarAnnualChargeTiming = prepaid
                ? CalendarAnnualChargeTiming.AtCheckout
                : CalendarAnnualChargeTiming.AtBoundary,
            AutomaticDiscountBasisPoints = 800
        },
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Year,
            IntervalCount = 1,
            AnchorInstantUtc = LocalMidnight(2026, 9, 1),
            TimeZoneId = Zurich,
            AnchorDayOfMonth = 1,
            AnchorMinutesFromMidnight = 0
        },
        CurrentPeriodStartUtc = new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = LocalMidnight(2026, 9, 1),
        InitialChargeAmountMinor = prepaid ? 19_736 + DiscountedAnnualMinor : 19_736,
        InitialChargeProrated = true,
        ProrationDays = 7,
        ProrationTotalDays = 31,
        PendingAnnualPeriod = new PendingAnnualPeriod
        {
            StartUtc = LocalMidnight(2026, 9, 1),
            EndUtc = LocalMidnight(2027, 9, 1),
            AmountMinor = DiscountedAnnualMinor,
            NetAmountMinor = DiscountedAnnualMinor,
            GrossAmountMinor = 1_140_000,
            BuiltInDiscountMinor = 91_200,
            IsPrepaid = prepaid
        }
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new() { DunningMaxAttempts = 4 };

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
