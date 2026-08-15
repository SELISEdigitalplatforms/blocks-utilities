using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Turning a chosen plan into a subscription, and the things that must be true of the result
/// years later.
/// </summary>
public sealed class SubscriptionCreationServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero));

    private Plan _plan = NewPlan();
    private SubscriptionDetail? _created;

    public SubscriptionCreationServiceTests()
    {
        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "professional", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _plan);

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice);

        _accounts
            .Setup(repository => repository.GetOrCreateAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount account, CancellationToken _) => account);

        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionDetail, CancellationToken>(
                (subscription, _) => _created = subscription)
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task The_currency_comes_from_the_price_and_is_fixed()
    {
        var result = await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.CurrencyCode.Should().Be("CHF");
    }

    [Fact]
    public async Task The_organization_comes_from_the_caller_not_the_request()
    {
        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.OrganizationId.Should().Be(OrganizationId,
            "accepting an organization from the body would let anyone subscribe on another " +
            "organization's behalf");
    }

    [Fact]
    public async Task The_plan_snapshot_is_a_copy_not_a_reference()
    {
        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _plan.Entitlements[0].Limit = 1;
        _plan.Meters[0].IncludedQuantity = 1;

        _created!.Plan.Entitlements[0].Limit.Should().Be(500);
        _created.Plan.Meters[0].IncludedQuantity.Should().Be(500,
            "editing the catalogue must not change what an existing subscriber holds");
    }

    [Fact]
    public async Task A_subscription_starts_granting_nothing()
    {
        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.Status.Should().Be(SubscriptionStatus.Incomplete,
            "nobody has paid yet, so walking away must leave nothing granted");
        _created.Version.Should().Be(1);
    }

    [Fact]
    public async Task The_order_id_is_derived_from_the_subscription()
    {
        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.OrderId.Should().Be($"sub:{_created.ItemId}",
            "a crash between raising a charge and recording it leaves this as the only way " +
            "back to the payment");
    }

    [Fact]
    public async Task Usage_is_metered_monthly_even_on_a_yearly_plan()
    {
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var price = NewPrice();
                price.Interval = BillingInterval.Year;

                return price;
            });

        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.FeeSchedule.Interval.Should().Be(BillingInterval.Year);
        _created.UsageSchedule.Interval.Should().Be(BillingInterval.Month,
            "waiting a year to settle metered usage is a year of unsecured credit");
    }

    [Fact]
    public async Task A_missing_quantity_takes_the_plans_default()
    {
        var request = NewRequest();
        request.Quantities.Clear();

        await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        _created!.QuantityItems.Should().ContainSingle()
            .Which.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task A_quantity_beyond_the_plans_maximum_is_refused()
    {
        _plan.QuantityItems[0].MaxQuantity = 10;

        var request = NewRequest();
        request.Quantities[0].Quantity = 11;

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _subscriptions.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_quantity_for_an_unknown_item_is_refused()
    {
        var request = NewRequest();
        request.Quantities[0].ItemKey = "not-an-item";

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_quantity_invalid");
    }

    [Fact]
    public async Task An_unknown_time_zone_is_refused_before_anything_is_written()
    {
        var request = NewRequest();
        request.TimeZoneId = "Middle/Earth";

        var result = await Service().CreateAsync(
            request, Context(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _subscriptions.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an unusable schedule must not become a stored subscription");
    }

    [Fact]
    public async Task A_trial_carries_its_own_capped_grants()
    {
        _plan.TrialDays = 14;
        _plan.TrialGrants =
        [
            new TrialMeterGrant { MeterKey = "screening", IncludedQuantity = 25 }
        ];

        await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        _created!.Trial!.EndsAtUtc.Should().Be(new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc));
        _created.Trial.Grants.Should().ContainSingle()
            .Which.IncludedQuantity.Should().Be(25,
                "each unit costs the seller real money, so an uncapped trial is a direct loss");
    }

    [Fact]
    public async Task A_second_live_subscription_is_a_conflict()
    {
        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("subscription_already_active");
    }

    [Fact]
    public async Task A_price_belonging_to_another_plan_is_not_found()
    {
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var price = NewPrice();
                price.PlanId = "another-plan";

                return price;
            });

        var result = await Service().CreateAsync(
            NewRequest(), Context(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
    }

    [Fact]
    public void The_period_amount_multiplies_quantity_by_the_snapshotted_unit_price()
    {
        var subscription = new SubscriptionDetail
        {
            Price = new PriceSnapshot { QuantityItemKey = "seat", UnitAmountMinor = 8900 },
            QuantityItems =
            [
                new SubscriptionQuantityItem
                {
                    ItemKey = "seat",
                    Quantity = 12,
                    UnitAmountMinor = 8900
                }
            ]
        };

        SubscriptionAmountCalculator.PeriodAmountMinor(subscription)
            .Should().Be(106_800);
    }

    [Fact]
    public void A_discount_can_reach_zero_but_never_go_below_it()
    {
        var subscription = new SubscriptionDetail
        {
            Price = new PriceSnapshot { UnitAmountMinor = 1_000 },
            Discount = new DiscountTerms
            {
                Kind = DiscountKind.FixedAmount,
                AmountMinor = 5_000
            }
        };

        SubscriptionAmountCalculator.PeriodAmountMinor(subscription)
            .Should().Be(0, "a negative charge is a refund, and one must never arrive by " +
                            "arithmetic");
    }

    private SubscriptionCreationService Service() => new(
        _catalogue.Object,
        _subscriptions.Object,
        _accounts.Object,
        new CreateSubscriptionRequestValidator(),
        NullLogger<SubscriptionCreationService>.Instance,
        _time);

    private static SubscriptionContext Context() =>
        new(TenantId, OrganizationId, "actor-1", "user-1");

    private static CreateSubscriptionRequest NewRequest() => new()
    {
        PlanCode = "professional",
        PriceId = "price-1",
        TimeZoneId = "Europe/Zurich",
        Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = 12 }]
    };

    private static Plan NewPlan() => new()
    {
        ItemId = "plan-1",
        TenantId = TenantId,
        Code = "professional",
        DisplayName = "Professional",
        Status = CatalogueStatus.Active,
        Version = 3,
        QuantityItems =
        [
            new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat", DefaultQuantity = 1 }
        ],
        Meters =
        [
            new PlanMeter
            {
                MeterKey = "screening",
                UnitLabel = "screening",
                IncludedQuantity = 500,
                ThresholdPercents = [80, 100]
            }
        ],
        Entitlements =
        [
            new PlanEntitlement
            {
                Key = "pep_screening",
                LimitKind = EntitlementLimitKind.Count,
                Limit = 500,
                MeterKey = "screening"
            }
        ]
    };

    private static Price NewPrice() => new()
    {
        ItemId = "price-1",
        TenantId = TenantId,
        PlanId = "plan-1",
        CurrencyCode = "CHF",
        UnitAmountMinor = 8900,
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        QuantityItemKey = "seat",
        Status = CatalogueStatus.Active
    };
}
