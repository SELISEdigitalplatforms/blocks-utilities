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

    private Task<PriceTermsExtension> ExtendDiscountAsync(
        DateTime nowUtc,
        int basisPoints) =>
        new ExistingSubscriberPriceTerms(
                _subscriptions.Object,
                new SubscriptionOutboxEventFactory(),
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>(
                    options => options.CurrentValue == new SubscriptionOptions()),
                new ControlledTimeProvider(new DateTimeOffset(nowUtc)),
                NullLogger<ExistingSubscriberPriceTerms>.Instance)
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
