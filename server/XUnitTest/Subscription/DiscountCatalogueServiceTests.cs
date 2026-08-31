using FluentAssertions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription;

/// <summary>
/// Authoring a discount, and the restrictions that would make one unredeemable.
/// </summary>
/// <remarks>
/// The failure this is mostly about: a discount restricted to a plan or price that does not exist is
/// accepted happily and then refuses every redemption with
/// <c>subscription_discount_not_applicable</c> — an error that says nothing about the typo that
/// caused it. The portal picks from a list and cannot make that mistake; a script, an API client, or
/// an identifier copied from another environment can.
/// </remarks>
public sealed class DiscountCatalogueServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string CustomerOrganizationId = "org-customer";

    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private Discount? _created;

    public DiscountCatalogueServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _catalogue
            .Setup(repository => repository.ListPlansAsync(
                TenantId, OrganizationId,
                It.IsAny<PlanCatalogueFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Plan("plan-pro", "pro"), Plan("plan-team", "team")]);

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-pro-yearly", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price("price-pro-yearly", "plan-pro"));

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-team-monthly", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price("price-team-monthly", "plan-team"));

        _discounts
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<Discount>(), It.IsAny<CancellationToken>()))
            .Callback<Discount, CancellationToken>((discount, _) => _created = discount)
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task A_standard_discount_persists_an_explicit_precedence()
    {
        var request = Request();
        request.CampaignPrecedence = CampaignPrecedence.ReplaceBuiltIn;

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.Campaign.Kind.Should().Be(CampaignKind.Standard);
        _created.Campaign.Precedence.Should().Be(CampaignPrecedence.ReplaceBuiltIn);
        _created.Campaign.PrecedenceConfigured.Should().BeTrue();
        result.Value!.CampaignPrecedenceConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task An_omitted_standard_precedence_preserves_the_legacy_plan_policy()
    {
        var result = await Service().CreateAsync(Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.Campaign.PrecedenceConfigured.Should().BeFalse();
        result.Value!.CampaignPrecedence.Should().Be("BestDiscount");
        result.Value.CampaignPrecedenceConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Authoring_for_another_organization_validates_against_that_organizations_catalogue()
    {
        // The console authoring a customer's discount. Resolved against the organization named in the
        // request — validating against the console's own catalogue reported the customer's plans as
        // unknown, on a discount that was then stored under the customer's scope anyway.
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                CustomerOrganizationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(
                    TenantId, CustomerOrganizationId, "actor-1", "user-1")));

        _catalogue
            .Setup(repository => repository.ListPlansAsync(
                TenantId, CustomerOrganizationId,
                It.IsAny<PlanCatalogueFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Plan("plan-customer", "customer-only")]);

        var request = Request();
        request.OrganizationId = CustomerOrganizationId;
        request.ApplicablePlanCodes = ["customer-only"];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "the plan exists in the organization the discount is being authored for");
        _created!.OrganizationId.Should().Be(CustomerOrganizationId);

        // The exact argument matters: this is the whole bug.
        _contextResolver.Verify(resolver => resolver.ResolveAsync(
            It.IsAny<string>(), CustomerOrganizationId, It.IsAny<CancellationToken>()), Times.Once);
        _contextResolver.Verify(resolver => resolver.ResolveAsync(
            It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_plan_from_another_organization_is_still_refused_when_authoring_for_it()
    {
        // The other half: resolving the requested organization must not become "validate against
        // nothing". A code naming a plan that organization does not have is still unredeemable.
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                CustomerOrganizationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(
                    TenantId, CustomerOrganizationId, "actor-1", "user-1")));

        _catalogue
            .Setup(repository => repository.ListPlansAsync(
                TenantId, CustomerOrganizationId,
                It.IsAny<PlanCatalogueFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Plan("plan-customer", "customer-only")]);

        var request = Request();
        request.OrganizationId = CustomerOrganizationId;
        request.ApplicablePlanCodes = ["pro"];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_applicability_invalid");
    }

    [Fact]
    public async Task A_caller_naming_an_organization_it_may_not_write_to_is_scoped_to_its_own()
    {
        // The resolver honours a named organization only for the console and answers with the
        // caller's own otherwise. Storing the *requested* value would have let anybody write a
        // discount into somebody else's catalogue scope.
        var request = Request();
        request.OrganizationId = "org-somebody-else";

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().Be(OrganizationId, "the resolver's answer, not the ask");
    }

    [Fact]
    public async Task A_tenant_wide_discount_stays_tenant_wide()
    {
        // Null is a scope rather than an organization, and it has to survive resolution — which
        // always answers with a concrete organization.
        var result = await Service().CreateAsync(Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.OrganizationId.Should().BeNull();
    }

    [Fact]
    public async Task An_unrestricted_discount_needs_no_catalogue_lookup()
    {
        // The ordinary case, and the one that must not pay for this check.
        var result = await Service().CreateAsync(Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _catalogue.Verify(repository => repository.ListPlansAsync(
            It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<PlanCatalogueFilter>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_discount_restricted_to_real_plans_and_prices_is_accepted()
    {
        var request = Request();
        request.ApplicablePlanCodes = ["pro"];
        request.ApplicablePriceIds = ["price-pro-yearly"];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.ApplicablePriceIds.Should().Equal("price-pro-yearly");
    }

    [Fact]
    public async Task A_plan_code_that_does_not_exist_is_refused()
    {
        var request = Request();
        request.ApplicablePlanCodes = ["por"];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_applicability_invalid");
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorMessage.Should().Contain("por", "the author has to be told which one is wrong");
        _created.Should().BeNull();
    }

    [Fact]
    public async Task A_price_that_does_not_exist_is_refused()
    {
        var request = Request();
        request.ApplicablePriceIds = ["price-typo"];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_applicability_invalid");
        _created.Should().BeNull();
    }

    [Fact]
    public async Task A_price_belonging_to_another_organizations_plan_reads_as_unknown()
    {
        // Not "forbidden": a refusal that distinguished the two would confirm that somebody else's
        // price exists to anyone willing to guess identifiers.
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-elsewhere", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price("price-elsewhere", "plan-not-visible"));

        var request = Request();
        request.ApplicablePriceIds = ["price-elsewhere"];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_applicability_invalid");
        result.ErrorMessage.Should().NotContain("plan-not-visible");
    }

    [Fact]
    public async Task A_price_outside_the_named_plans_is_refused_as_unredeemable()
    {
        // Both restrictions narrow, so a price on an unlisted plan matches nothing. Accepted, this
        // would be a discount that exists and can never be used.
        var request = Request();
        request.ApplicablePlanCodes = ["pro"];
        request.ApplicablePriceIds = ["price-team-monthly"];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_applicability_invalid");
        result.ErrorMessage.Should().Contain("could never be redeemed");
    }

    [Fact]
    public async Task A_price_named_without_any_plan_restriction_is_accepted()
    {
        // Price-only is a legitimate authoring choice: "8% off the yearly price, whatever plan it is
        // on" needs no plan list.
        var request = Request();
        request.ApplicablePriceIds = ["price-team-monthly"];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private DiscountCatalogueService Service() => new(
        _discounts.Object,
        _catalogue.Object,
        _contextResolver.Object,
        new CreateDiscountRequestValidator(),
        new UpdateDiscountRequestValidator());

    private static CreateDiscountRequest Request() => new()
    {
        Code = "launch25",
        DisplayName = "Launch offer",
        Kind = DiscountKind.Percent,
        PercentBasisPoints = 2_500
    };

    private static Plan Plan(string planId, string code) => new()
    {
        ItemId = planId,
        TenantId = TenantId,
        Code = code,
        DisplayName = code,
        Status = CatalogueStatus.Active
    };

    private static Price Price(string priceId, string planId) => new()
    {
        ItemId = priceId,
        TenantId = TenantId,
        PlanId = planId,
        CurrencyCode = "CHF",
        UnitAmountMinor = 10_000,
        Status = CatalogueStatus.Active
    };
}
