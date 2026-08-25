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

    /// <summary>Set to make the next charge decline, so a dunning retry can be exercised.</summary>
    private bool _declineCharge;

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
            .ReturnsAsync(() => _declineCharge
                ? SubscriptionOperationResult<string>.Failure(
                    PaymentFailureKind.ProviderRejected,
                    "card_declined",
                    "The card was declined.",
                    "corr-1")
                : SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
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
    /// A declined renewal and its dunning retry have to be attempts at the same period, or the
    /// same month gets billed twice.
    /// </summary>
    /// <remarks>
    /// The attempt number is deliberately part of the idempotency key, so the two differ there.
    /// What must not move is the period they are both attempts at, which the order id carries.
    /// </remarks>
    [Fact]
    public async Task A_retry_after_a_real_decline_charges_the_same_period()
    {
        var subscription = CalendarSubscription(SubscriptionStatus.Active);

        _declineCharge = true;
        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.PastDue);
        var declinedAmount = _charge!.AmountMinor;
        var declinedOrderId = _charge.OrderId;

        // Dunning picks it up again, in the state the decline left it in.
        subscription.Status = SubscriptionStatus.PastDue;
        subscription.DunningAttemptCount = 1;
        _declineCharge = false;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(declinedAmount);
        _charge.OrderId.Should().Be(declinedOrderId,
            "both are attempts at the same period, so they settle the same order");
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 10, 1));
    }

    /// <summary>
    /// The conversion equivalent, and the one that loses the stub if recognition is keyed on
    /// status: a declined conversion is no longer <c>Trialing</c> when dunning retries it.
    /// </summary>
    [Fact]
    public async Task A_declined_conversion_still_retries_the_stub_it_owes()
    {
        _time.Advance(
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        var subscription = ConvertingTrial();

        _declineCharge = true;
        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(3_445);
        _transition!.NewStatus.Should().Be(SubscriptionStatus.PastDue);

        // Dunning retries it, and the subscription is no longer Trialing.
        subscription.Status = SubscriptionStatus.PastDue;
        subscription.DunningAttemptCount = 1;
        _declineCharge = false;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(3_445,
            "the unpaid August stub is still what is owed, not whatever month the clock reached");
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _transition.InitialChargeAmountMinor.Should().Be(3_445);
        _transition.ProrationDays.Should().Be(12);
        _transition.NextFeeBillingAtUtc.Should().Be(LocalMidnight(2026, 9, 1),
            "September is still due, and is charged separately once the stub is paid");
    }

    /// <summary>
    /// Once the stub is paid the conversion is over, whatever the status happens to be.
    /// </summary>
    [Fact]
    public async Task A_paid_conversion_is_not_retried_as_one()
    {
        _time.Advance(
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        var subscription = ConvertingTrial();
        await Service().RenewAsync(subscription, CancellationToken.None);

        // The subscription as the conversion left it: first charge recorded, September due.
        subscription.Status = SubscriptionStatus.Active;
        subscription.InitialChargeAmountMinor = _transition!.InitialChargeAmountMinor;
        subscription.CurrentPeriodStartUtc = _transition.CurrentPeriodStartUtc!.Value;
        subscription.CurrentPeriodEndUtc = _transition.CurrentPeriodEndUtc!.Value;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(8_900, "September, in full");
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 10, 1));
    }

    /// <summary>
    /// A promotion that was live when the trial ended must price the period it covered, however
    /// late the sweep runs — otherwise the same contractual period costs two different amounts
    /// depending on worker latency.
    /// </summary>
    [Fact]
    public async Task A_conversion_is_priced_at_the_trial_end_not_the_sweep_clock()
    {
        _time.Advance(
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        var subscription = ConvertingTrial();
        subscription.Discount = new DiscountTerms
        {
            Code = "welcome",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_000,
            // Live when the trial ended on 20 August; lapsed by the time this sweep ran.
            ExpiresAtUtc = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc)
        };

        await Service().RenewAsync(subscription, CancellationToken.None);

        // 20% off the 3445 stub is 2756, not the undiscounted 3445.
        _charge!.AmountMinor.Should().Be(2_756);
        _transition!.InitialChargeDiscountApplied.Should().BeTrue();
        _transition.DiscountPeriodsApplied.Should().Be(1);
    }

    /// <summary>A trial ending 20 August, converting a fortnight after it should have.</summary>
    private static SubscriptionDetail ConvertingTrial()
    {
        var subscription = CalendarSubscription(SubscriptionStatus.Trialing);
        subscription.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
            RequiresPaymentMethod = false
        };

        return subscription;
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

    /// <summary>
    /// The one that loses money if it is wrong. A sweep held up past the next month boundary must
    /// still bill the stub it owes rather than starting from today and writing off the days in
    /// between.
    /// </summary>
    [Fact]
    public async Task A_conversion_discovered_after_the_month_boundary_still_bills_the_stub_it_owes()
    {
        // The trial ended 20 August. Nothing ran until 2 September.
        _time.Advance(
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        var subscription = CalendarSubscription(SubscriptionStatus.Trialing);
        subscription.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
            RequiresPaymentMethod = false
        };

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(3_445,
            "20 to 31 August is 12 of 31 dates, and nobody has billed them");
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 1));

        // Left due again immediately, so the next pass raises September as its own charge rather
        // than this one silently swallowing it.
        _transition.NextFeeBillingAtUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _transition.NextFeeBillingAtUtc.Should().BeBefore(_time.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// And the charge that follows it must be a different one, not a replay of the stub.
    /// </summary>
    [Fact]
    public async Task The_month_after_a_late_conversion_is_charged_separately()
    {
        _time.Advance(
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        var subscription = CalendarSubscription(SubscriptionStatus.Trialing);
        subscription.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
            RequiresPaymentMethod = false
        };

        await Service().RenewAsync(subscription, CancellationToken.None);
        var stubKey = _idempotencyKey;

        // The subscription as the conversion left it: active, its first charge recorded, and due
        // for September. The recorded first charge is what ends the conversion — not the status,
        // which a decline would have moved to PastDue instead.
        subscription.Status = SubscriptionStatus.Active;
        subscription.InitialChargeAmountMinor = _transition!.InitialChargeAmountMinor;
        subscription.CurrentPeriodStartUtc = _transition.CurrentPeriodStartUtc!.Value;
        subscription.CurrentPeriodEndUtc = _transition.CurrentPeriodEndUtc!.Value;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _charge!.AmountMinor.Should().Be(8_900, "September is a whole month");
        _idempotencyKey.Should().NotBe(stubKey,
            "August and September are different periods and must not collide on one key");
        _transition.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 10, 1));
    }

    /// <summary>
    /// A trial's first paid period is the one case where the opening charge is not knowable at
    /// signup, so it is recorded when it finally happens.
    /// </summary>
    [Fact]
    public async Task A_conversion_records_what_the_first_paid_period_actually_cost()
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

        _transition!.InitialChargeAmountMinor.Should().Be(3_445);
        _transition.InitialChargeProrated.Should().BeTrue();
        _transition.ProrationDays.Should().Be(12);
        _transition.ProrationTotalDays.Should().Be(31,
            "the fraction describes the month the trial ended in, not the one it started in");
    }

    [Fact]
    public async Task An_ordinary_renewal_never_rewrites_what_the_first_charge_was()
    {
        var subscription = CalendarSubscription(SubscriptionStatus.Active);

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transition!.InitialChargeAmountMinor.Should().BeNull();
        _transition.InitialChargeProrated.Should().BeNull();
        _transition.ProrationDays.Should().BeNull(
            "a renewal months later must not overwrite what the original checkout froze");
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
