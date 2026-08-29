using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Phase 4: the buyer-facing explanation a purchase preview attaches to a campaign discount.
/// </summary>
/// <remarks>
/// <c>POST /subscriptions/preview</c> already prices a campaign code exactly as a real signup
/// would -- it shares <c>BuildSubscriptionAsync</c> with <see cref="SubscriptionCreationService.CreateAsync"/>
/// wholesale, so there is only one pricing path to get right, and Phase 1 through 3b's own tests
/// already cover it. What a preview response could not do until now is say *why* a figure is
/// temporary: a buyer reading a zero due-now or a discounted renewal has no way to tell from the
/// numbers alone that either reverts. This suite is entirely about that explanation, not the
/// pricing underneath it.
/// </remarks>
public sealed class SubscriptionPreviewCampaignTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<ISubscriptionBillingProfileGuard> _billingProfile = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero));

    public SubscriptionPreviewCampaignTests()
    {
        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _billingProfile
            .Setup(guard => guard.ContactDefaultsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingContactDefaults("Ada Byron", "billing@northwind.example"));

        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "professional", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPlan());
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-monthly", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMonthlyPrice());
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-yearly", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewCalendarYearlyPrice());

        _accounts
            .Setup(repository => repository.GetOrCreateAndReconcileAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount account, CancellationToken _) => account);

        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);
        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);
    }

    [Fact]
    public async Task A_standard_discounts_preview_carries_no_campaign_explanation()
    {
        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "launch25", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                Terms = new DiscountTerms
                {
                    Code = "launch25", Kind = DiscountKind.Percent, PercentBasisPoints = 2_500
                }
            });

        var request = NewRequest("price-monthly");
        request.DiscountCode = "launch25";

        var result = await Service().PreviewAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Campaign.Should().BeNull();
    }

    [Fact]
    public async Task A_free_opening_period_previews_its_own_explanation_and_entitlement_cap()
    {
        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "free1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ItemId = "discount-free1",
                Version = 1,
                Terms = new DiscountTerms
                {
                    Code = "free1", Kind = DiscountKind.Percent, PercentBasisPoints = 10_000
                },
                Campaign = new CampaignTerms
                {
                    Kind = CampaignKind.FreeOpeningCalendarPeriod,
                    EntitlementOverride = new CampaignEntitlementOverride
                    {
                        EntitlementKey = "seats", Limit = 1
                    },
                    RedeemableFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    RedeemableUntilUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            });

        var request = NewRequest("price-monthly");
        request.DiscountCode = "free1";

        var result = await Service().PreviewAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var campaign = result.Value!.Campaign;
        campaign.Should().NotBeNull();
        campaign!.Kind.Should().Be("FreeOpeningCalendarPeriod");
        campaign.Description.Should().NotBeNullOrWhiteSpace();
        // The same instant the confirm's own subscription would carry as CurrentPeriodEndUtc --
        // read back rather than recomputed, so this cannot name a different boundary than the one
        // the entitlement override and the change-lock actually read.
        campaign.DiscountEndsAtUtc.Should().Be(result.Value.PeriodEndUtc);
        campaign.TemporaryEntitlementKey.Should().Be("seats");
        campaign.TemporaryEntitlementLimit.Should().Be(1);
    }

    [Fact]
    public async Task A_first_annual_period_preview_names_the_discounted_years_own_end_not_the_stubs()
    {
        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "annual1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ItemId = "discount-annual1",
                Version = 1,
                Terms = new DiscountTerms
                {
                    Code = "annual1", Kind = DiscountKind.Percent, PercentBasisPoints = 1_500
                },
                ApplicablePriceIds = ["price-yearly"],
                Campaign = new CampaignTerms
                {
                    Kind = CampaignKind.FirstAnnualPeriod,
                    RedeemableFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    RedeemableUntilUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            });

        var request = NewRequest("price-yearly");
        request.DiscountCode = "annual1";

        var result = await Service().PreviewAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var campaign = result.Value!.Campaign;
        campaign.Should().NotBeNull();
        campaign!.Kind.Should().Be("FirstAnnualPeriod");
        // The buyer signed up mid-month, so PeriodEndUtc on the response is the STUB's end -- the
        // discount instead ends when the discounted year it also bought ends, which is what a
        // buyer actually needs to know standard pricing resumes at.
        result.Value.PendingAnnualPeriod.Should().NotBeNull();
        campaign.DiscountEndsAtUtc.Should().Be(result.Value.PendingAnnualPeriod!.EndUtc);
        campaign.DiscountEndsAtUtc.Should().NotBe(result.Value.PeriodEndUtc);
        // FirstAnnualPeriod never carries an entitlement override -- nothing to describe here even
        // though the field exists on the response shape.
        campaign.TemporaryEntitlementKey.Should().BeNull();
    }

    private SubscriptionCreationService Service() => new(
        _catalogue.Object,
        _subscriptions.Object,
        _discounts.Object,
        _accounts.Object,
        new CreateSubscriptionRequestValidator(),
        NullLogger<SubscriptionCreationService>.Instance,
        _time,
        billingProfile: _billingProfile.Object);

    private static SubscriptionContext Context() =>
        new(TenantId, OrganizationId, "actor-1", "user-1");

    private static CreateSubscriptionRequest NewRequest(string priceId) => new()
    {
        PlanCode = "professional",
        PriceId = priceId,
        TimeZoneId = "Europe/Zurich",
        Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = 1 }]
    };

    private static Plan NewPlan() => new()
    {
        ItemId = "plan-1",
        TenantId = TenantId,
        Code = "professional",
        DisplayName = "Professional",
        Status = CatalogueStatus.Active,
        Version = 3,
        Entitlements =
        [
            new PlanEntitlement { Key = "seats", LimitKind = EntitlementLimitKind.Count, Limit = 5 }
        ],
        QuantityItems =
        [
            new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat", DefaultQuantity = 1 }
        ]
    };

    private static Price NewMonthlyPrice() => new()
    {
        ItemId = "price-monthly",
        TenantId = TenantId,
        PlanId = "plan-1",
        CurrencyCode = "CHF",
        UnitAmountMinor = 8_900,
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        QuantityItemKey = "seat",
        Status = CatalogueStatus.Active
    };

    /// <summary>A signup on 14 August 2026 -- mid-month, so this cadence always has a stub.</summary>
    private static Price NewCalendarYearlyPrice() => new()
    {
        ItemId = "price-yearly",
        TenantId = TenantId,
        PlanId = "plan-1",
        CurrencyCode = "CHF",
        UnitAmountMinor = 96_000,
        Interval = BillingInterval.Year,
        IntervalCount = 1,
        BillingAlignment = BillingAlignment.CalendarMonth,
        CalendarStubBasePriceId = "price-monthly",
        CalendarStubBaseUnitAmountMinor = 8_900,
        QuantityItemKey = "seat",
        Status = CatalogueStatus.Active
    };
}
