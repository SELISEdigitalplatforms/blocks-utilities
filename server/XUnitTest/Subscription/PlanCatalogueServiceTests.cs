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
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _currencyResolver = new();
    private Plan? _created;
    private Plan? _updated;

    public PlanCatalogueServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _catalogue
            .Setup(repository => repository.TryCreatePlanAsync(
                It.IsAny<Plan>(),
                It.IsAny<CancellationToken>()))
            .Callback<Plan, CancellationToken>((plan, _) => _created = plan)
            .ReturnsAsync(true);

        // A plan with no prices yet is the ordinary state right after it is authored, and every
        // path that maps a plan to a response reads this.
        _catalogue
            .Setup(repository => repository.ListPricesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _currencyResolver
            .Setup(resolver => resolver.TryConvertBack(
                It.IsAny<long>(),
                It.IsAny<string>(),
                out It.Ref<decimal>.IsAny))
            .Returns(true);
    }

    /// <summary>
    /// The regression this guards: the price was written and then the plan read back without
    /// naming the organization it belongs to. The console writes under its own fixed
    /// organization, so the read reported the plan as missing — after the price had already
    /// been committed. The caller saw a failure for work that had actually succeeded.
    /// </summary>
    [Fact]
    public async Task Pricing_another_organizations_plan_returns_that_plan_rather_than_missing()
    {
        var plan = StoredPlan();
        plan.OrganizationId = "org-9";

        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        _catalogue
            .Setup(repository => repository.TryCreatePriceAsync(
                It.IsAny<Price>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // The console only reaches org-9 by naming it. Resolving without the name leaves it in
        // its own fixed organization, which is exactly what the price path used to do — so the
        // default setup from the constructor stands in for that.
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), "org-9", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, "org-9", "actor-1", "user-1")));

        var result = await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                OrganizationId = "org-9",
                CurrencyCode = "CHF",
                UnitAmountMinor = 8900,
                QuantityItemKey = "seat"
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "the price was created, so reporting it as missing is a lie about work that landed");
        result.Value!.PlanId.Should().Be("plan-1");
    }

    /// <summary>
    /// The lookup is keyed by tenant and plan alone, so without this any caller in the tenant
    /// could put a price on another organization's plan.
    /// </summary>
    [Fact]
    public async Task Pricing_a_plan_belonging_to_someone_else_is_refused()
    {
        var plan = StoredPlan();
        plan.OrganizationId = "somebody-else";

        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        var result = await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 8900,
                QuantityItemKey = "seat"
            },
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        _catalogue.Verify(
            repository => repository.TryCreatePriceAsync(
                It.IsAny<Price>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a refused price must not reach the store");
    }

    [Fact]
    public async Task A_tenant_wide_plan_can_still_be_priced()
    {
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPlan());
        _catalogue
            .Setup(repository => repository.TryCreatePriceAsync(
                It.IsAny<Price>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 8900,
                QuantityItemKey = "seat"
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_price_forwards_its_requested_organization_to_context_resolution()
    {
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPlan());

        await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                OrganizationId = "org-9",
                CurrencyCode = "CHF",
                UnitAmountMinor = 8900,
                QuantityItemKey = "seat"
            },
            "corr-1",
            CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "only the console gets to act on this, and that is decided downstream");
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
    public async Task Creating_an_organization_plan_resolves_the_requested_scope_and_persists_the_answer()
    {
        var request = NewPlan();
        request.OrganizationId = "org-requested";

        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                "corr-1", "org-requested", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, "org-resolved", "actor-1", "user-1")));

        var result = await Service().CreatePlanAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().Be("org-resolved",
            "the organization resolver, not an untrusted request body, owns catalogue scope");
        _contextResolver.Verify(
            resolver => resolver.ResolveAsync(
                "corr-1", "org-requested", It.IsAny<CancellationToken>()),
            Times.Once);
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
    public async Task An_out_of_range_tax_rate_is_rejected()
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
                QuantityItemKey = "seat",
                TaxRateBasisPoints = 10_001
            },
            "corr-1",
            CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_price_invalid");
        _catalogue.Verify(
            repository => repository.TryCreatePriceAsync(
                It.IsAny<Price>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_tax_rate_without_a_mode_is_refused_rather_than_assumed()
    {
        // The one combination that cannot be interpreted. The same "145 at 7.7%" is either CHF 156.17
        // or CHF 145.00 to the customer, so the author answers the question rather than discovering
        // the answer on an invoice.
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPlan());

        var result = await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 14_500,
                QuantityItemKey = "seat",
                TaxRateBasisPoints = 770
            },
            "corr-1",
            CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_price_invalid");
        _catalogue.Verify(
            repository => repository.TryCreatePriceAsync(
                It.IsAny<Price>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(TaxMode.Exclusive)]
    [InlineData(TaxMode.Inclusive)]
    public async Task A_priced_tax_mode_is_stored_as_authored(TaxMode mode)
    {
        // Stored rather than normalized, because everything downstream — the snapshot a subscriber
        // is sold on, the invoice they download — reads it from here.
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPlan());

        Price? stored = null;
        _catalogue
            .Setup(repository => repository.TryCreatePriceAsync(
                It.IsAny<Price>(), It.IsAny<CancellationToken>()))
            .Callback((Price price, CancellationToken _) => stored = price)
            .ReturnsAsync(true);

        var result = await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 14_500,
                QuantityItemKey = "seat",
                TaxRateBasisPoints = 770,
                TaxMode = mode
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stored!.TaxMode.Should().Be(mode);
        stored.TaxRateBasisPoints.Should().Be(770);
    }

    [Fact]
    public async Task An_untaxed_price_needs_no_mode()
    {
        // Asking how to add nothing would make every flat, untaxed price answer a question about
        // tax it does not have.
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPlan());
        _catalogue
            .Setup(repository => repository.TryCreatePriceAsync(
                It.IsAny<Price>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 14_500,
                QuantityItemKey = "seat"
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Existing_price_tax_can_change_without_touching_subscriber_snapshots()
    {
        var plan = StoredPlan();
        var price = new Price
        {
            ItemId = "price-1",
            TenantId = TenantId,
            PlanId = plan.ItemId,
            Status = CatalogueStatus.Active,
            Version = 4
        };
        _catalogue.Setup(repository => repository.GetPriceAsync(
            TenantId, "price-1", It.IsAny<CancellationToken>())).ReturnsAsync(price);
        _catalogue.Setup(repository => repository.GetPlanAsync(
            TenantId, plan.ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        _catalogue.Setup(repository => repository.TryUpdatePriceTaxAsync(
            TenantId, "price-1", 4, 770, TaxMode.Inclusive,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _subscriptions.Setup(repository => repository.AnySubscriberAsync(
            TenantId, plan.ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Service().UpdatePriceTaxAsync(
            "price-1",
            new UpdatePriceTaxRequest
            {
                TaxRateBasisPoints = 770,
                TaxMode = TaxMode.Inclusive
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _subscriptions.Verify(repository => repository.AnySubscriberAsync(
            TenantId, plan.ItemId, It.IsAny<CancellationToken>()), Times.Once);
        _subscriptions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_existing_price_can_be_given_an_automatic_discount()
    {
        var plan = StoredPlan();
        StoredPriceFor(plan, version: 4);
        _catalogue.Setup(repository => repository.TryUpdatePriceAutomaticDiscountAsync(
            TenantId, "price-1", 4, 800, AutomaticDiscountCombination.Additive,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Service().UpdatePriceDiscountAsync(
            "price-1",
            new UpdatePriceDiscountRequest
            {
                AutomaticDiscountBasisPoints = 800,
                QuantityDiscountCombination = AutomaticDiscountCombination.Additive
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _catalogue.Verify(repository => repository.TryUpdatePriceAutomaticDiscountAsync(
            TenantId, "price-1", 4, 800, AutomaticDiscountCombination.Additive,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Clearing_an_automatic_discount_clears_its_combination_too()
    {
        // Zero means "no discount", and a price with no discount must not keep a stale answer to how
        // that discount would have combined with a band.
        var plan = StoredPlan();
        StoredPriceFor(plan, version: 2);
        _catalogue.Setup(repository => repository.TryUpdatePriceAutomaticDiscountAsync(
            TenantId, "price-1", 2, null, null,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Service().UpdatePriceDiscountAsync(
            "price-1",
            new UpdatePriceDiscountRequest
            {
                AutomaticDiscountBasisPoints = 0,
                QuantityDiscountCombination = AutomaticDiscountCombination.Additive
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _catalogue.Verify(repository => repository.TryUpdatePriceAutomaticDiscountAsync(
            TenantId, "price-1", 2, null, null,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_discount_without_a_combination_is_stored_as_the_safe_one()
    {
        var plan = StoredPlan();
        StoredPriceFor(plan, version: 1);
        _catalogue.Setup(repository => repository.TryUpdatePriceAutomaticDiscountAsync(
            TenantId, "price-1", 1, 800, AutomaticDiscountCombination.BestDiscount,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Service().UpdatePriceDiscountAsync(
            "price-1",
            new UpdatePriceDiscountRequest { AutomaticDiscountBasisPoints = 800 },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public async Task An_automatic_discount_outside_nought_to_a_hundred_percent_is_refused(
        int basisPoints)
    {
        var result = await Service().UpdatePriceDiscountAsync(
            "price-1",
            new UpdatePriceDiscountRequest { AutomaticDiscountBasisPoints = basisPoints },
            "corr-1",
            CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_price_discount_invalid");
    }

    [Fact]
    public async Task A_price_that_moved_while_being_saved_reports_a_conflict()
    {
        var plan = StoredPlan();
        StoredPriceFor(plan, version: 7);
        _catalogue.Setup(repository => repository.TryUpdatePriceAutomaticDiscountAsync(
            TenantId, "price-1", 7, 800, It.IsAny<AutomaticDiscountCombination?>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Service().UpdatePriceDiscountAsync(
            "price-1",
            new UpdatePriceDiscountRequest { AutomaticDiscountBasisPoints = 800 },
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("subscription_price_discount_conflict");
    }

    [Fact]
    public async Task A_new_price_can_be_authored_with_an_automatic_discount()
    {
        Price? stored = null;
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPlan());
        _catalogue
            .Setup(repository => repository.TryCreatePriceAsync(
                It.IsAny<Price>(), It.IsAny<CancellationToken>()))
            .Callback<Price, CancellationToken>((price, _) => stored = price)
            .ReturnsAsync(true);

        var result = await Service().CreatePriceAsync(
            new CreatePriceRequest
            {
                PlanId = "plan-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 100_000,
                AutomaticDiscountBasisPoints = 800,
                QuantityDiscountCombination = AutomaticDiscountCombination.Additive
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stored!.AutomaticDiscountBasisPoints.Should().Be(800);
        stored.QuantityDiscountCombination.Should().Be(AutomaticDiscountCombination.Additive);
    }

    /// <summary>
    /// An active price on the plan, wired for the two lookups every price editor performs.
    /// </summary>
    private Price StoredPriceFor(Plan plan, int version)
    {
        var price = new Price
        {
            ItemId = "price-1",
            TenantId = TenantId,
            PlanId = plan.ItemId,
            Status = CatalogueStatus.Active,
            Version = version
        };

        _catalogue.Setup(repository => repository.GetPriceAsync(
            TenantId, "price-1", It.IsAny<CancellationToken>())).ReturnsAsync(price);
        _catalogue.Setup(repository => repository.GetPlanAsync(
            TenantId, plan.ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        return price;
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
            null,
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound,
            "a forbidden response would confirm the identifier exists somewhere else");
    }

    [Fact]
    public async Task A_caller_without_an_organization_is_refused()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Unavailable,
                "subscription_organization_missing",
                "An organization is required."));

        var result = await Service().ListPlansAsync(null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_organization_missing");
    }

    [Fact]
    public async Task A_requested_organization_on_list_plans_is_forwarded_to_context_resolution()
    {
        _catalogue
            .Setup(repository => repository.ListPlansAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Plan>());

        await Service().ListPlansAsync("org-9", "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    [Fact]
    public async Task A_requested_organization_on_get_plan_is_forwarded_to_context_resolution()
    {
        await Service().GetPlanAsync("plan-1", "org-9", "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    [Fact]
    public async Task A_plan_nobody_has_subscribed_to_can_be_rewritten()
    {
        StorePlan(StoredPlan());
        _catalogue
            .Setup(repository => repository.TryUpdatePlanAsync(
                TenantId, "plan-1", 1, It.IsAny<Plan>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, Plan, CancellationToken>(
                (_, _, _, plan, _) => _updated = plan)
            .ReturnsAsync(true);

        var result = await Service().UpdatePlanAsync(
            "plan-1",
            EditedPlan(),
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _updated!.DisplayName.Should().Be("Professional plus");
        _updated.Entitlements[0].Limit.Should().Be(500);
    }

    [Fact]
    public async Task Editing_keeps_the_code_and_scope_the_request_cannot_name()
    {
        var stored = StoredPlan();
        stored.OrganizationId = "org-1";
        StorePlan(stored);
        _catalogue
            .Setup(repository => repository.TryUpdatePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Plan>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, Plan, CancellationToken>(
                (_, _, _, plan, _) => _updated = plan)
            .ReturnsAsync(true);

        await Service().UpdatePlanAsync("plan-1", EditedPlan(), "corr-1", CancellationToken.None);

        _updated!.Code.Should().Be("professional",
            "configuration points at the code, so an edit may not move it");
        _updated.OrganizationId.Should().Be("org-1",
            "changing the scope would move the plan out from under whoever can see it");
    }

    /// <summary>
    /// The reason editing is closed at all: subscribing copies the plan's terms onto the
    /// subscription and bills from that copy, so an edit cannot reach anyone already on it.
    /// </summary>
    [Fact]
    public async Task A_plan_that_has_been_subscribed_to_is_refused()
    {
        StorePlan(StoredPlan());
        _subscriptions
            .Setup(repository => repository.AnySubscriberAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Service().UpdatePlanAsync(
            "plan-1",
            EditedPlan(),
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("subscription_plan_in_use");
        _catalogue.Verify(
            repository => repository.TryUpdatePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Plan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Another_organizations_plan_reads_as_missing_rather_than_editable()
    {
        var stored = StoredPlan();
        stored.OrganizationId = "somebody-else";
        StorePlan(stored);

        var result = await Service().UpdatePlanAsync(
            "plan-1",
            EditedPlan(),
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
    }

    [Fact]
    public async Task An_edit_that_lost_the_version_race_is_refused_rather_than_overwriting()
    {
        StorePlan(StoredPlan());
        _catalogue
            .Setup(repository => repository.TryUpdatePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Plan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().UpdatePlanAsync(
            "plan-1",
            EditedPlan(),
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("subscription_plan_changed");
    }

    [Fact]
    public async Task An_edit_is_held_to_the_same_rules_as_authoring()
    {
        StorePlan(StoredPlan());
        var request = EditedPlan();
        request.Entitlements[0].MeterKey = "not-a-meter";

        var result = await Service().UpdatePlanAsync(
            "plan-1",
            request,
            "corr-1",
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ValidationErrors.Should().NotBeNull();
        _catalogue.Verify(
            repository => repository.TryUpdatePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Plan>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an edit that stored something a create would have refused would be a hole");
    }

    [Fact]
    public async Task A_requested_organization_on_update_is_forwarded_to_context_resolution()
    {
        StorePlan(StoredPlan());
        var request = EditedPlan();
        request.OrganizationId = "org-9";

        await Service().UpdatePlanAsync("plan-1", request, "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_plan_that_has_been_subscribed_to_says_so_when_read()
    {
        StorePlan(StoredPlan());
        _subscriptions
            .Setup(repository => repository.AnySubscriberAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Service().GetPlanAsync(
            "plan-1",
            null,
            "corr-1",
            CancellationToken.None);

        result.Value!.HasSubscribers.Should().BeTrue(
            "the portal has to be able to say why editing is closed before offering it");
    }

    /// <summary>
    /// Purely a label: naming a predecessor neither migrates anyone nor touches either plan's
    /// editability or purchasability, so this only checks that the link and its resolved name
    /// come back — not that anything about the plans themselves changed.
    /// </summary>
    [Fact]
    public async Task A_plan_can_name_a_predecessor_for_display()
    {
        var predecessor = StoredPlan();
        predecessor.ItemId = "plan-0";
        predecessor.DisplayName = "Legacy professional";
        StorePlan(predecessor);

        var request = NewPlan();
        request.PredecessorPlanId = "plan-0";

        var result = await Service().CreatePlanAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.PredecessorPlanId.Should().Be("plan-0");
        result.Value!.PredecessorDisplayName.Should().Be("Legacy professional",
            "the caller should not need a second lookup to render a link");
    }

    [Fact]
    public async Task An_organization_scoped_plan_can_be_duplicated_as_its_successor()
    {
        var predecessor = StoredPlan();
        predecessor.ItemId = "plan-0";
        predecessor.OrganizationId = "org-9";
        predecessor.DisplayName = "Legacy professional";
        StorePlan(predecessor);

        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                "corr-1", "org-9", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, "org-9", "actor-1", "user-1")));

        var request = NewPlan();
        request.OrganizationId = "org-9";
        request.PredecessorPlanId = "plan-0";

        var result = await Service().CreatePlanAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().Be("org-9");
        _created.PredecessorPlanId.Should().Be("plan-0");
        result.Value!.PredecessorDisplayName.Should().Be("Legacy professional");
    }

    [Fact]
    public async Task A_predecessor_that_does_not_exist_is_refused()
    {
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, "missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Plan?)null);

        var request = NewPlan();
        request.PredecessorPlanId = "missing";

        var result = await Service().CreatePlanAsync(request, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("subscription_plan_predecessor_not_found");
        _catalogue.Verify(
            repository => repository.TryCreatePlanAsync(It.IsAny<Plan>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a stray or foreign id must never be stored, even as a label");
    }

    [Fact]
    public async Task A_predecessor_scoped_to_another_organization_is_refused()
    {
        var predecessor = StoredPlan();
        predecessor.ItemId = "plan-9";
        predecessor.OrganizationId = "org-9";
        StorePlan(predecessor);

        var request = NewPlan();
        request.PredecessorPlanId = "plan-9";

        var result = await Service().CreatePlanAsync(request, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("subscription_plan_predecessor_not_found",
            "not found and not visible are reported the same way, so an organization boundary " +
            "cannot be discovered through what error comes back");
    }

    [Fact]
    public async Task A_plan_reports_the_successor_that_named_it_as_a_predecessor()
    {
        var stored = StoredPlan();
        StorePlan(stored);

        var successor = StoredPlan();
        successor.ItemId = "plan-2";
        successor.DisplayName = "Professional (2026)";
        successor.PredecessorPlanId = stored.ItemId;

        _catalogue
            .Setup(repository => repository.FindSuccessorPlanAsync(
                TenantId, stored.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(successor);

        var result = await Service().GetPlanAsync(stored.ItemId, null, "corr-1", CancellationToken.None);

        result.Value!.SuccessorPlanId.Should().Be("plan-2");
        result.Value.SuccessorDisplayName.Should().Be("Professional (2026)");
    }

    [Fact]
    public async Task A_plan_with_no_successor_reports_none()
    {
        StorePlan(StoredPlan());
        _catalogue
            .Setup(repository => repository.FindSuccessorPlanAsync(
                TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Plan?)null);

        var result = await Service().GetPlanAsync("plan-1", null, "corr-1", CancellationToken.None);

        result.Value!.SuccessorPlanId.Should().BeNull();
        result.Value.SuccessorDisplayName.Should().BeNull();
    }

    [Fact]
    public async Task Archiving_a_price_takes_it_off_the_menu()
    {
        StorePlan(StoredPlan());
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Price { ItemId = "price-1", TenantId = TenantId, PlanId = "plan-1" });
        _catalogue
            .Setup(repository => repository.TryArchivePriceAsync(
                TenantId, "price-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Service().ArchivePriceAsync(
            "price-1", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _catalogue.Verify(
            repository => repository.TryArchivePriceAsync(
                TenantId, "price-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Archiving_a_price_that_is_already_off_the_menu_is_a_conflict()
    {
        // Reported rather than treated as success, so a second click does not read as having
        // retired something a moment ago.
        StorePlan(StoredPlan());
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Price { ItemId = "price-1", TenantId = TenantId, PlanId = "plan-1" });
        _catalogue
            .Setup(repository => repository.TryArchivePriceAsync(
                TenantId, "price-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().ArchivePriceAsync(
            "price-1", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("subscription_price_not_active");
    }

    [Fact]
    public async Task Another_organizations_price_cannot_be_archived()
    {
        // The price lookup is keyed only by tenant, so without the plan's visibility check any
        // caller in the tenant could retire another organization's price.
        var plan = StoredPlan();
        plan.OrganizationId = "org-9";
        StorePlan(plan);
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Price { ItemId = "price-1", TenantId = TenantId, PlanId = "plan-1" });

        var result = await Service().ArchivePriceAsync(
            "price-1", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        _catalogue.Verify(
            repository => repository.TryArchivePriceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_price_that_does_not_exist_is_not_found()
    {
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Price?)null);

        var result = await Service().ArchivePriceAsync(
            "price-1", null, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
    }

    private void StorePlan(Plan plan) =>
        _catalogue
            .Setup(repository => repository.GetPlanAsync(
                TenantId, plan.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

    private PlanCatalogueService Service() => new(
        _catalogue.Object,
        _subscriptions.Object,
        _contextResolver.Object,
        new CreatePlanRequestValidator(),
        new UpdatePlanRequestValidator(),
        new CreatePriceRequestValidator(_currencyResolver.Object),
        new PlanResponseMapper(),
        NullLogger<PlanCatalogueService>.Instance);

    private static Plan StoredPlan() => new()
    {
        ItemId = "plan-1",
        TenantId = TenantId,
        Code = "professional",
        Version = 1,
        QuantityItems = [new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat" }]
    };

    /// <summary>The same plan as <see cref="NewPlan"/>, renamed — an edit names no code.</summary>
    private static UpdatePlanRequest EditedPlan()
    {
        var authored = NewPlan();

        return new UpdatePlanRequest
        {
            DisplayName = "Professional plus",
            QuantityItems = authored.QuantityItems,
            Meters = authored.Meters,
            Entitlements = authored.Entitlements,
            TrialGrants = authored.TrialGrants
        };
    }

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
