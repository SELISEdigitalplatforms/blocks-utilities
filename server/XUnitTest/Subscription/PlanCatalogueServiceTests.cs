using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription;

/// <summary>
/// Authoring a plan, and the rules that stop an unusable one being sold.
/// </summary>
public sealed class PlanCatalogueServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _currencyResolver = new();
    private Plan? _created;

    public PlanCatalogueServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _catalogue
            .Setup(repository => repository.TryCreatePlanAsync(
                It.IsAny<Plan>(),
                It.IsAny<CancellationToken>()))
            .Callback<Plan, CancellationToken>((plan, _) => _created = plan)
            .ReturnsAsync(true);

        _currencyResolver
            .Setup(resolver => resolver.TryConvertBack(
                It.IsAny<long>(),
                It.IsAny<string>(),
                out It.Ref<decimal>.IsAny))
            .Returns(true);
    }

    [Fact]
    public async Task A_plan_keeps_the_products_own_vocabulary()
    {
        var request = NewPlan();
        request.QuantityItems[0].UnitLabel = "workspace";
        request.Meters[0].MeterKey = "envelope";
        request.Entitlements[0].MeterKey = "envelope";

        var result = await Service().CreatePlanAsync(
            request,
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.QuantityItems[0].UnitLabel.Should().Be("workspace");
        _created.Meters[0].MeterKey.Should().Be("envelope",
            "the platform stores the product's word and never substitutes its own");
    }

    [Fact]
    public async Task Plan_features_are_stored_verbatim()
    {
        var request = NewPlan();
        request.FeaturesJson = """{"qualified_signature":true,"max_templates":50}""";

        await Service().CreatePlanAsync(request, "corr-1", CancellationToken.None);

        _created!.FeaturesJson.Should().Be(request.FeaturesJson,
            "nothing here interprets a feature, so nothing may reshape one either");
    }

    [Fact]
    public async Task A_plan_scoped_to_no_organization_serves_the_whole_tenant()
    {
        await Service().CreatePlanAsync(NewPlan(), "corr-1", CancellationToken.None);

        _created!.OrganizationId.Should().BeNull();
    }

    [Fact]
    public async Task A_duplicate_plan_code_is_a_conflict()
    {
        _catalogue
            .Setup(repository => repository.TryCreatePlanAsync(
                It.IsAny<Plan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().CreatePlanAsync(
            NewPlan(),
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("subscription_plan_exists");
    }

    [Fact]
    public async Task An_entitlement_naming_an_unknown_meter_is_rejected()
    {
        var request = NewPlan();
        request.Entitlements[0].MeterKey = "not-a-meter";

        var result = await Service().CreatePlanAsync(
            request,
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _catalogue.Verify(
            repository => repository.TryCreatePlanAsync(
                It.IsAny<Plan>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "nothing is written when the plan could never work");
    }

    [Fact]
    public async Task Malformed_plan_features_are_rejected()
    {
        var request = NewPlan();
        request.FeaturesJson = "not json";

        var result = await Service().CreatePlanAsync(
            request,
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ValidationErrors.Should().NotBeNull();
    }

    [Fact]
    public async Task Overlapping_rate_tiers_are_rejected()
    {
        var request = NewPlan();
        request.Meters[0].RateTables =
        [
            new MeterRateTableRequest
            {
                CurrencyCode = "CHF",
                Tiers =
                [
                    new MeterTierRequest { UpToQuantity = 500, UnitAmountMinor = 100 },
                    new MeterTierRequest { UpToQuantity = 200, UnitAmountMinor = 90 }
                ]
            }
        ];

        var result = await Service().CreatePlanAsync(
            request,
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
    }

    [Fact]
    public async Task An_unbounded_tier_anywhere_but_last_is_rejected()
    {
        var request = NewPlan();
        request.Meters[0].RateTables =
        [
            new MeterRateTableRequest
            {
                CurrencyCode = "CHF",
                Tiers =
                [
                    new MeterTierRequest { UpToQuantity = null, UnitAmountMinor = 100 },
                    new MeterTierRequest { UpToQuantity = 500, UnitAmountMinor = 90 }
                ]
            }
        ];

        var result = await Service().CreatePlanAsync(
            request,
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
    }

    [Fact]
    public async Task A_price_in_a_currency_payments_cannot_charge_is_refused()
    {
        _currencyResolver
            .Setup(resolver => resolver.TryConvertBack(
                It.IsAny<long>(),
                It.IsAny<string>(),
                out It.Ref<decimal>.IsAny))
            .Returns(false);

        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId,
                "plan-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPlan());

        var result = await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                CurrencyCode = "XYZ",
                UnitAmountMinor = 8900,
                QuantityItemKey = "seat"
            },
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("subscription_price_invalid");
        _catalogue.Verify(
            repository => repository.TryCreatePriceAsync(
                It.IsAny<Price>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "catching this at authoring time is the point — at checkout it reaches a customer");
    }

    [Fact]
    public async Task A_price_for_an_unknown_quantity_item_is_rejected()
    {
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId,
                "plan-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPlan());

        var result = await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 8900,
                QuantityItemKey = "not-an-item"
            },
            "corr-1",
            CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_quantity_item_unknown");
    }

    [Fact]
    public async Task Another_organizations_plan_reports_as_missing()
    {
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId,
                "plan-2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Plan
            {
                ItemId = "plan-2",
                TenantId = TenantId,
                OrganizationId = "somebody-else"
            });

        var result = await Service().GetPlanAsync(
            "plan-2",
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound,
            "a forbidden response would confirm the identifier exists somewhere else");
    }

    [Fact]
    public async Task A_caller_without_an_organization_is_refused()
    {
        _contextResolver
            .Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns(SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Unavailable,
                "subscription_organization_missing",
                "An organization is required."));

        var result = await Service().ListPlansAsync("corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_organization_missing");
    }

    private PlanCatalogueService Service() => new(
        _catalogue.Object,
        _contextResolver.Object,
        new CreatePlanRequestValidator(),
        new CreatePriceRequestValidator(_currencyResolver.Object),
        new PlanResponseMapper(),
        NullLogger<PlanCatalogueService>.Instance);

    private static Plan StoredPlan() => new()
    {
        ItemId = "plan-1",
        TenantId = TenantId,
        Code = "professional",
        QuantityItems = [new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat" }]
    };

    private static CreatePlanRequest NewPlan() => new()
    {
        Code = "professional",
        DisplayName = "Professional",
        QuantityItems =
        [
            new PlanQuantityItemRequest { ItemKey = "seat", UnitLabel = "seat" }
        ],
        Meters =
        [
            new PlanMeterRequest
            {
                MeterKey = "screening",
                DisplayName = "Screenings",
                UnitLabel = "screening",
                IncludedQuantity = 500,
                ThresholdPercents = [80, 100]
            }
        ],
        Entitlements =
        [
            new PlanEntitlementRequest
            {
                Key = "pep_screening",
                LimitKind = EntitlementLimitKind.Count,
                Limit = 500,
                MeterKey = "screening"
            }
        ]
    };
}
