using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// How many periods of a limited promotion a calendar-aligned yearly subscription spends.
/// </summary>
/// <remarks>
/// Exactly one, however the money arrives. A promotion is spent by the charge that reduced money,
/// and each of the three routes reduces it once — so counting has to follow the payment rather than
/// the period, or a subscriber who paid up front loses a month of their discount for doing so.
/// <para>
/// The prepaid boundary is the case that gets this wrong most easily: it opens a discounted year
/// and looks exactly like a renewal, but takes nothing, because the money came in a month earlier.
/// </para>
/// </remarks>
public sealed class CalendarAnnualDiscountConsumptionTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string Zurich = "Europe/Zurich";
    private const long DiscountedAnnualMinor = 1_026_000;

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();

    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero));

    private SubscriptionTransition? _transition;

    public CalendarAnnualDiscountConsumptionTests()
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
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
    }

    /// <summary>
    /// The defect: a prepaid year was counted twice — once by the checkout that collected it, and
    /// again by the boundary that merely opened it.
    /// </summary>
    [Fact]
    public async Task A_prepaid_year_opening_spends_no_further_discount_period()
    {
        var subscription = InStub(prepaid: true);

        // The checkout already spent the promotion when it took the money.
        subscription.DiscountPeriodsApplied = 1;

        await Renewal().RenewAsync(subscription, CancellationToken.None);

        _transition!.DiscountPeriodsApplied.Should().Be(1,
            "nothing was charged here, so nothing more of the promotion was used");
    }

    /// <summary>
    /// The control. An unpaid year is charged at its boundary, and that charge is where the
    /// promotion is spent — so this one must increment.
    /// </summary>
    [Fact]
    public async Task An_unpaid_year_spends_its_discount_period_at_the_boundary()
    {
        var subscription = InStub(prepaid: false);

        await Renewal().RenewAsync(subscription, CancellationToken.None);

        _transition!.DiscountPeriodsApplied.Should().Be(1,
            "this boundary is the charge that reduced money");
    }

    /// <summary>
    /// End to end for the prepaid route: activation spends the period, the boundary does not, and a
    /// one-period promotion is therefore gone after exactly one bill rather than none or two.
    /// </summary>
    [Fact]
    public async Task An_at_checkout_signup_spends_exactly_one_period_across_both_transitions()
    {
        var subscription = InStub(prepaid: false);
        subscription.Status = SubscriptionStatus.Incomplete;
        subscription.PendingAnnualPeriod!.CollectedWithCheckout = true;
        subscription.InitialChargeDiscountApplied = true;

        // Activation: the combined charge landed, so the promotion is spent here.
        await Activation().SettleLinkAsync(DueLink(), CancellationToken.None);
        var afterActivation = _transition!.DiscountPeriodsApplied ?? 0;

        afterActivation.Should().Be(1, "the charge that collected the year used the promotion");

        // The boundary, a month later, with the year now settled.
        var atBoundary = InStub(prepaid: true);
        atBoundary.DiscountPeriodsApplied = afterActivation;

        await Renewal().RenewAsync(atBoundary, CancellationToken.None);

        _transition!.DiscountPeriodsApplied.Should().Be(1,
            "one payment reduced by the promotion means one period of it spent");
    }

    private SubscriptionRenewalService Renewal() => new(
        _subscriptions.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new OptionsStub(),
        NullLogger<SubscriptionRenewalService>.Instance,
        _time);

    private SubscriptionPaymentLink DueLink() => new()
    {
        ItemId = "link-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        SubscriptionId = "sub-1",
        PaymentDetailId = "pay-1",
        OrderId = "sub:sub-1",
        Purpose = SubscriptionPaymentPurpose.InitialCharge,
        State = SubscriptionPaymentLinkState.Pending,
        CorrelationId = "corr-1"
    };

    private SubscriptionActivationProcessor Activation()
    {
        var links = new Mock<ISubscriptionPaymentLinkRepository>();
        var payments = new Mock<IPaymentRepository>();
        var storedMethods = new Mock<IStoredPaymentMethodRepository>();
        var accounts = new Mock<IBillingAccountRepository>();

        links
            .Setup(repository => repository.TrySettleAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<SubscriptionPaymentLinkState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-1",
                PaymentStatus = PaymentStatuses.Authorized,
                WebhookConfirmedAtUtc = _time.GetUtcNow().UtcDateTime
            });

        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var subscription = InStub(prepaid: false);
                subscription.Status = SubscriptionStatus.Incomplete;
                subscription.PendingAnnualPeriod!.CollectedWithCheckout = true;
                subscription.InitialChargeDiscountApplied = true;

                return subscription;
            });

        return new SubscriptionActivationProcessor(
            links.Object,
            _subscriptions.Object,
            accounts.Object,
            new SubscriptionOutboxEventFactory(),
            payments.Object,
            storedMethods.Object,
            new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
            NullLogger<SubscriptionActivationProcessor>.Instance,
            _time);
    }

    private static DateTime LocalMidnight(int year, int month, int day) =>
        BillingLocalTime.ToUtc(
            new DateTime(year, month, day, 0, 0, 0),
            TimeZoneInfo.FindSystemTimeZoneById(Zurich));

    /// <summary>A 25 August signup on a 10%-off promotion, inside its stub.</summary>
    private static SubscriptionDetail InStub(bool prepaid) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        OrderId = "sub:sub-1",
        CorrelationId = "corr-1",
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
                : CalendarAnnualChargeTiming.AtBoundary
        },
        Discount = new DiscountTerms
        {
            Code = "welcome",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 1_000,
            DurationPeriods = 1
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
            PromotionalDiscountMinor = 114_000,
            DiscountApplied = true,
            CollectedWithCheckout = prepaid,
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
