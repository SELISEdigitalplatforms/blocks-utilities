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
/// Extending a discount or tax edit to the subscribers already on a price.
/// </summary>
/// <remarks>
/// Guards against a subscriber being repriced without notice — billed on new terms at a renewal
/// days after the edit, told a date the bill does not keep, or moved onto terms nobody extended to
/// them — and against a subscriber the operator meant to reach being silently passed over.
/// </remarks>
public sealed class ExistingSubscriberPriceTermsTests
{
    private const string TenantId = "tenant-1";
    private const string PriceId = "price-1";

    private static readonly DateTime December1 = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime January1 = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly List<(PendingPriceTerms Pending, SubscriptionOutboxEvent Event)> _written = [];
    private List<SubscriptionDetail> _onPrice = [];

    public ExistingSubscriberPriceTermsTests()
    {
        _subscriptions
            .Setup(repository => repository.ListRenewableOnPriceAsync(
                TenantId, PriceId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _onPrice);

        _subscriptions
            .Setup(repository => repository.TrySetPendingPriceTermsAsync(
                TenantId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PendingPriceTerms>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, PendingPriceTerms, SubscriptionOutboxEvent, CancellationToken>(
                (_, _, _, pending, outboxEvent, _) => _written.Add((pending, outboxEvent)))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task A_renewal_three_weeks_away_is_the_one_the_new_terms_are_announced_for()
    {
        _onPrice = [OnPrice()];

        var result = await ExtendDiscountAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), 1500);

        result.Scheduled.Should().Be(1);
        var (pending, outboxEvent) = _written.Single();
        pending.EffectiveAtUtc.Should().Be(December1,
            "the next renewal is well outside a week's notice, so it is the one billed on the new terms");
        pending.Terms.AutomaticDiscountBasisPoints.Should().Be(1500);
        outboxEvent.EventType.Should().Be(SubscriptionConstants.SubscriptionPriceTermsChangeScheduled,
            "the subscriber is told in the same write that schedules the change");
    }

    [Fact]
    public async Task A_renewal_inside_the_notice_period_renews_once_more_on_the_old_terms()
    {
        _onPrice = [OnPrice()];

        await ExtendDiscountAsync(new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc), 1500);

        _written.Single().Pending.EffectiveAtUtc.Should().Be(January1,
            "three days is less than the week a subscriber is owed, so the change waits a period");
    }

    [Fact]
    public async Task Cancelling_and_departing_subscribers_are_left_alone()
    {
        var cancelling = OnPrice("sub-cancelling");
        cancelling.CancelAtPeriodEnd = true;
        var departing = OnPrice("sub-departing");
        departing.PendingPlanChange = new PendingPlanChange { EffectiveAtUtc = December1 };
        _onPrice = [cancelling, departing];

        var result = await ExtendDiscountAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), 1500);

        result.Skipped.Should().Be(2);
        _written.Should().BeEmpty(
            "a subscription that will not renew and one leaving this price " +
            "were neither of them quoted these terms");
    }

    [Fact]
    public async Task An_offline_subscription_follows_a_price_terms_change_like_a_card_one()
    {
        var offline = OnPrice("sub-offline");
        offline.CollectionMethod = SubscriptionCollectionMethod.OfflineInvoice;
        _onPrice = [offline];

        var result = await ExtendDiscountAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), 1500);

        result.Skipped.Should().Be(0);
        result.Scheduled.Should().Be(1);
    }

    [Fact]
    public async Task A_discount_edit_keeps_a_tax_edit_already_extended_to_the_subscriber()
    {
        var subscription = OnPrice();
        subscription.PendingPriceTerms = new PendingPriceTerms
        {
            PriceId = PriceId,
            Terms = new PriceTerms
            {
                AutomaticDiscountBasisPoints = 800,
                TaxRateBasisPoints = 810,
                TaxMode = TaxMode.Exclusive
            },
            EffectiveAtUtc = December1
        };
        _onPrice = [subscription];

        await ExtendDiscountAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), 1500);

        var terms = _written.Single().Pending.Terms;
        terms.AutomaticDiscountBasisPoints.Should().Be(1500);
        terms.TaxRateBasisPoints.Should().Be(810,
            "the tax change the subscriber was already told about must not be undone by a discount edit");
    }

    [Fact]
    public async Task A_second_edit_never_brings_an_announced_date_forward()
    {
        var subscription = OnPrice();
        subscription.PendingPriceTerms = new PendingPriceTerms
        {
            PriceId = PriceId,
            Terms = new PriceTerms { AutomaticDiscountBasisPoints = 1000 },
            EffectiveAtUtc = January1
        };
        _onPrice = [subscription];

        await ExtendDiscountAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), 1500);

        _written.Single().Pending.EffectiveAtUtc.Should().Be(January1,
            "the subscriber was promised the old terms until January, and a later edit cannot shorten that");
    }

    [Fact]
    public async Task A_subscriber_changing_at_the_same_instant_is_read_again_rather_than_passed_over()
    {
        var subscription = OnPrice();
        _onPrice = [subscription];
        _subscriptions
            .SetupSequence(repository => repository.TrySetPendingPriceTermsAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<PendingPriceTerms>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        var reread = OnPrice();
        reread.Version = subscription.Version + 1;
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reread);

        var result = await ExtendDiscountAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), 1500);

        result.Scheduled.Should().Be(1,
            "a renewal or seat change racing the operator is ordinary and must not cost the subscriber their notice");
        _subscriptions.Verify(
            repository => repository.TrySetPendingPriceTermsAsync(
                TenantId, "sub-1", reread.Version, It.IsAny<PendingPriceTerms>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_overage_rate_cut_reprices_existing_subscribers_now_and_tells_them()
    {
        var subscription = OnPlan(unitAmountMinor: 200);
        List<MeterRateTable>? written = null;
        SubscriptionOutboxEvent? announced = null;
        _subscriptions
            .Setup(repository => repository.ListRenewableOnPlanAsync(
                TenantId, PlanId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([subscription]);
        _subscriptions
            .Setup(repository => repository.TrySetMeterRatesAsync(
                TenantId, "sub-1", subscription.Version, PlanId, MeterKey,
                It.IsAny<List<MeterRateTable>>(), It.IsAny<SubscriptionOutboxEvent?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, int, string, string, List<MeterRateTable>, SubscriptionOutboxEvent?, CancellationToken>(
                (_, _, _, _, _, tables, outboxEvent, _) => (written, announced) = (tables, outboxEvent))
            .ReturnsAsync(true);

        var result = await ApplyMeterRatesAsync(Rates(("CHF", 100)));

        result.Scheduled.Should().Be(1);
        written!.Single().Tiers.Single().UnitAmountMinor.Should().Be(100,
            "the subscriber's own plan copy is what the open period is rated from when it closes");
        announced.Should().NotBeNull("the subscriber is told in the same write that reprices them");
        announced!.EventType.Should().Be(SubscriptionConstants.SubscriptionOverageRatesChanged);
    }

    [Fact]
    public async Task An_overage_edit_that_leaves_the_subscribers_currency_alone_writes_nothing()
    {
        _subscriptions
            .Setup(repository => repository.ListRenewableOnPlanAsync(
                TenantId, PlanId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([OnPlan(unitAmountMinor: 200)]);

        var result = await ApplyMeterRatesAsync(Rates(("CHF", 200), ("EUR", 90)));

        result.Skipped.Should().Be(1);
        _subscriptions.Verify(
            repository => repository.TrySetMeterRatesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<List<MeterRateTable>>(),
                It.IsAny<SubscriptionOutboxEvent?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a CHF subscriber is never billed in EUR, so a EUR change is not news to them");
    }

    [Fact]
    public async Task An_overage_edit_that_drops_the_subscribers_currency_leaves_their_rates_alone()
    {
        _subscriptions
            .Setup(repository => repository.ListRenewableOnPlanAsync(
                TenantId, PlanId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([OnPlan(unitAmountMinor: 200)]);

        var result = await ApplyMeterRatesAsync(Rates(("EUR", 90)));

        result.Skipped.Should().Be(1, "removing their currency must not leave their overage unpriced");
    }

    [Fact]
    public async Task A_credit_price_change_in_the_subscribers_currency_is_written_and_announced()
    {
        var (written, announced) = SetUpCreditPush(OnPlanWithCredits(currency: "CHF", unitAmountMinor: 125));

        var result = await ApplyCreditsAsync(Credits(("CHF", 150)));

        result.Scheduled.Should().Be(1);
        written.Single()!.PriceIn("CHF")!.UnitAmountMinor.Should().Be(150);
        announced.Single().Should().NotBeNull();
        announced.Single()!.EventType.Should().Be(SubscriptionConstants.SubscriptionCreditTermsChanged);
    }

    [Fact]
    public async Task A_credit_change_only_in_another_currency_is_written_without_an_email()
    {
        var subscriber = OnPlanWithCredits(currency: "CHF", unitAmountMinor: 125);
        var (written, announced) = SetUpCreditPush(subscriber);

        await ApplyCreditsAsync(Credits(("CHF", 125), ("EUR", 120)));

        written.Should().ContainSingle("the copy is kept in step with the catalogue");
        announced.Single().Should().BeNull("a CHF subscriber can never be quoted the EUR price");
    }

    [Fact]
    public async Task A_subscriber_billed_by_invoice_is_never_emailed_about_credits()
    {
        var subscriber = OnPlanWithCredits(currency: "CHF", unitAmountMinor: 125);
        subscriber.CollectionMethod = SubscriptionCollectionMethod.OfflineInvoice;
        var (_, announced) = SetUpCreditPush(subscriber);

        await ApplyCreditsAsync(Credits(("CHF", 150)));

        announced.Single().Should().BeNull("an invoice-billed subscription cannot buy credits at all");
    }

    [Fact]
    public async Task Identical_credit_terms_are_skipped()
    {
        var (written, _) = SetUpCreditPush(OnPlanWithCredits(currency: "CHF", unitAmountMinor: 125));

        var result = await ApplyCreditsAsync(Credits(("CHF", 125)));

        result.Skipped.Should().Be(1);
        written.Should().BeEmpty();
    }

    private (List<MeterTopUpTerms?> Written, List<SubscriptionOutboxEvent?> Announced) SetUpCreditPush(
        SubscriptionDetail subscriber)
    {
        var written = new List<MeterTopUpTerms?>();
        var announced = new List<SubscriptionOutboxEvent?>();
        _subscriptions
            .Setup(repository => repository.ListRenewableOnPlanAsync(
                TenantId, PlanId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([subscriber]);
        _subscriptions
            .Setup(repository => repository.TrySetMeterTopUpAsync(
                TenantId, subscriber.ItemId, subscriber.Version, PlanId, MeterKey,
                It.IsAny<MeterTopUpTerms?>(), It.IsAny<SubscriptionOutboxEvent?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, int, string, string, MeterTopUpTerms?, SubscriptionOutboxEvent?, CancellationToken>(
                (_, _, _, _, _, terms, outboxEvent, _) =>
                {
                    written.Add(terms);
                    announced.Add(outboxEvent);
                })
            .ReturnsAsync(true);

        return (written, announced);
    }

    private Task<PriceTermsExtension> ApplyCreditsAsync(MeterTopUpTerms terms) =>
        new ExistingSubscriberPriceTerms(
                _subscriptions.Object,
                new SubscriptionOutboxEventFactory(),
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>(
                    options => options.CurrentValue == new SubscriptionOptions()),
                new ControlledTimeProvider(new DateTimeOffset(December1)),
                NullLogger<ExistingSubscriberPriceTerms>.Instance)
            .ApplyCreditTermsAsync(
                TenantId, PlanId, MeterKey, terms, "Operator", "actor-1", "user-1", "corr-1",
                CancellationToken.None);

    private static MeterTopUpTerms Credits(params (string Currency, long UnitAmountMinor)[] prices) => new()
    {
        Enabled = true,
        UnitPrices = [.. prices.Select(price => new MeterTopUpPrice
        {
            CurrencyCode = price.Currency,
            UnitAmountMinor = price.UnitAmountMinor
        })],
        MinQuantity = 10,
        MaxQuantity = 1_000
    };

    private static SubscriptionDetail OnPlanWithCredits(string currency, long unitAmountMinor)
    {
        var subscription = OnPlan(unitAmountMinor: 200);
        subscription.CurrencyCode = currency;
        subscription.Plan.Meters[0].TopUp = Credits((currency, unitAmountMinor));

        return subscription;
    }

    private const string PlanId = "plan-1";
    private const string MeterKey = "api-calls";

    private Task<PriceTermsExtension> ApplyMeterRatesAsync(List<MeterRateTable> rateTables) =>
        new ExistingSubscriberPriceTerms(
                _subscriptions.Object,
                new SubscriptionOutboxEventFactory(),
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>(
                    options => options.CurrentValue == new SubscriptionOptions()),
                new ControlledTimeProvider(new DateTimeOffset(December1)),
                NullLogger<ExistingSubscriberPriceTerms>.Instance)
            .ApplyMeterRatesAsync(
                TenantId, PlanId, MeterKey, rateTables, "Operator", "actor-1", "user-1", "corr-1",
                CancellationToken.None);

    private static List<MeterRateTable> Rates(params (string Currency, long UnitAmountMinor)[] rates) =>
        rates.Select(rate => new MeterRateTable
            {
                CurrencyCode = rate.Currency,
                Tiers = [new MeterTier { UnitAmountMinor = rate.UnitAmountMinor }]
            })
            .ToList();

    /// <summary>A CHF subscriber whose plan copy prices the meter's overage flat.</summary>
    private static SubscriptionDetail OnPlan(long unitAmountMinor)
    {
        var subscription = OnPrice();
        subscription.Plan = new PlanSnapshot
        {
            PlanId = PlanId,
            Code = "professional",
            DisplayName = "Professional",
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = MeterKey,
                    DisplayName = "API calls",
                    RateTables = Rates(("CHF", unitAmountMinor))
                }
            ]
        };

        return subscription;
    }

    [Fact]
    public void Terms_scheduled_before_amounts_could_change_leave_the_amount_alone()
    {
        var price = new PriceSnapshot { UnitAmountMinor = 8_900 };

        new PriceTerms { AutomaticDiscountBasisPoints = 1500 }.ApplyTo(price);

        price.UnitAmountMinor.Should().Be(8_900,
            "pending terms stored without an amount must not reprice the subscriber to zero");
        PriceTerms.Of(price).UnitAmountMinor.Should().Be(8_900);
    }

    [Fact]
    public async Task Each_subscriber_given_new_terms_has_the_change_on_their_own_timeline()
    {
        _onPrice = [OnPrice()];
        var audit = new Mock<ISubscriptionAuditTrail>();
        SubscriptionAuditEvent? recorded = null;
        audit
            .Setup(trail => trail.RecordAsync(It.IsAny<SubscriptionAuditEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionAuditEvent, CancellationToken>((auditEvent, _) => recorded = auditEvent)
            .Returns(Task.CompletedTask);

        await ExtendDiscountAsync(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), 1500, audit.Object);

        recorded.Should().NotBeNull("a customer asking why their price changed looks at their own timeline");
        recorded!.SubscriptionId.Should().Be("sub-1");
        recorded.Operation.Should().Be("PriceTermsScheduled");
        recorded.TargetPriceId.Should().Be(PriceId);
        recorded.EffectiveAtUtc.Should().Be(December1);
        recorded.ActorId.Should().Be("actor-1");
        recorded.Detail.Should().Be("Automatic discount 8% → 15%",
            "only what moved, so a discount edit does not read as a price change");
    }

    private Task<PriceTermsExtension> ExtendDiscountAsync(
        DateTime nowUtc,
        int basisPoints,
        ISubscriptionAuditTrail? audit = null) =>
        new ExistingSubscriberPriceTerms(
                _subscriptions.Object,
                new SubscriptionOutboxEventFactory(),
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>(
                    options => options.CurrentValue == new SubscriptionOptions()),
                new ControlledTimeProvider(new DateTimeOffset(nowUtc)),
                NullLogger<ExistingSubscriberPriceTerms>.Instance,
                audit)
            .ExtendAsync(
                TenantId,
                PriceId,
                terms => new PriceTerms
                {
                    AutomaticDiscountBasisPoints = basisPoints,
                    TaxRateBasisPoints = terms.TaxRateBasisPoints,
                    TaxMode = terms.TaxMode
                },
                "Operator",
                "actor-1",
                "user-1",
                "corr-1",
                CancellationToken.None);

    /// <summary>A monthly subscriber on an 8% discount, paid through 1 December.</summary>
    private static SubscriptionDetail OnPrice(string id = "sub-1") => new()
    {
        ItemId = id,
        TenantId = TenantId,
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Active,
        Version = 3,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
        Price = new PriceSnapshot
        {
            PriceId = PriceId,
            CurrencyCode = "CHF",
            UnitAmountMinor = 8_900,
            AutomaticDiscountBasisPoints = 800
        },
        CurrentPeriodEndUtc = December1,
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        }
    };
}
