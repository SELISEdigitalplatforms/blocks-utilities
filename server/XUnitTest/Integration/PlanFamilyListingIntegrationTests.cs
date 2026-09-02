using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// Listing a plan catalogue narrowed to one product family.
/// </summary>
/// <remarks>
/// Against a real MongoDB because what is being tested is how the family narrowing composes with
/// the organization-over-tenant resolution the listing already performs, and that resolution is a
/// property of the documents actually stored — two plans sharing a code, one owned by an
/// organization and one by the tenant. A mocked repository would be the thing under test.
/// <para>
/// The case that matters most is the one where the two plans sharing a code sit in *different*
/// families. Narrowing inside the query rather than after the resolution would answer with a plan
/// that subscribing could never select.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class PlanFamilyListingIntegrationTests
{
    private const string Organization = "org-1";

    private readonly SubscriptionCatalogueRepository _catalogue;

    public PlanFamilyListingIntegrationTests(MongoIntegrationFixture fixture) =>
        _catalogue = new SubscriptionCatalogueRepository(fixture.DbContextProvider);

    /// <summary>Omitting the family code lists everything, exactly as it did before.</summary>
    [Fact]
    public async Task No_family_code_lists_every_family()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "starter", "growth", 1));
        await Seed(tenantId, Plan(tenantId, "pro", "growth", 2));
        await Seed(tenantId, Plan(tenantId, "lab", "research", 1));
        await Seed(tenantId, Plan(tenantId, "solo", family: null, rank: null));

        var listed = await _catalogue.ListPlansAsync(
            tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None);

        listed.Select(plan => plan.Code)
            .Should()
            .BeEquivalentTo(["starter", "pro", "lab", "solo"]);
    }

    [Fact]
    public async Task A_family_code_lists_only_that_family()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "starter", "growth", 1));
        await Seed(tenantId, Plan(tenantId, "pro", "growth", 2));
        await Seed(tenantId, Plan(tenantId, "lab", "research", 1));
        await Seed(tenantId, Plan(tenantId, "solo", family: null, rank: null));

        var listed = await _catalogue.ListPlansAsync(
            tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None, "growth");

        listed.Select(plan => plan.Code).Should().Equal("starter", "pro");
    }

    /// <summary>
    /// Ordered by rank, not by code, because ranking a family is what the rank is for.
    /// </summary>
    /// <remarks>
    /// Seeded so that the two orders disagree: by code "enterprise" would come first, by rank it
    /// comes last. A test whose codes happened to sort into rank order would pass either way.
    /// </remarks>
    [Fact]
    public async Task A_family_is_ordered_by_rank_rather_than_by_code()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "enterprise", "growth", 3));
        await Seed(tenantId, Plan(tenantId, "starter", "growth", 1));
        await Seed(tenantId, Plan(tenantId, "pro", "growth", 2));

        var listed = await _catalogue.ListPlansAsync(
            tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None, "growth");

        listed.Select(plan => plan.Code).Should().Equal("starter", "pro", "enterprise");
    }

    /// <summary>
    /// The narrowing happens after the organization-over-tenant collapse, so it can never
    /// advertise a plan subscribing would not resolve.
    /// </summary>
    /// <remarks>
    /// The organization's own "pro" sits in the premium family and shadows the tenant's "pro",
    /// which sits in the basic family. Asking for the basic family must report nothing: the plan
    /// that would resolve for this organization is the premium one, and offering the tenant's
    /// shadowed record would be a choice subscribing cannot honour.
    /// <para>
    /// Filtering inside the query would leave the tenant's plan as the only member of its code
    /// group and return it — which is the defect this ordering exists to prevent.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_shadowed_plans_family_is_not_offered_to_the_organization_that_shadows_it()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "pro", "basic", 1));
        await Seed(tenantId, Plan(tenantId, "pro", "premium", 1, organizationId: Organization));

        var basic = await _catalogue.ListPlansAsync(
            tenantId, Organization, PlanCatalogueFilter.Active, CancellationToken.None, "basic");

        basic.Should().BeEmpty(
            "the plan this organization would resolve for the code 'pro' is not in the basic family");

        var premium = await _catalogue.ListPlansAsync(
            tenantId, Organization, PlanCatalogueFilter.Active, CancellationToken.None, "premium");

        premium.Should().ContainSingle()
            .Which.OrganizationId.Should().Be(Organization);
    }

    /// <summary>
    /// A tenant-wide reader is unaffected by an organization's shadowing, so it sees the tenant's
    /// own plan in its own family.
    /// </summary>
    [Fact]
    public async Task A_tenant_wide_reader_still_sees_the_tenants_own_family()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "pro", "basic", 1));
        await Seed(tenantId, Plan(tenantId, "pro", "premium", 1, organizationId: Organization));

        var listed = await _catalogue.ListPlansAsync(
            tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None, "basic");

        listed.Should().ContainSingle().Which.OrganizationId.Should().BeNull();
    }

    /// <summary>
    /// Matched exactly. A family code is stored as authored, so two differing only in case are two
    /// families, and folding case would merge them.
    /// </summary>
    [Fact]
    public async Task A_family_code_is_matched_case_sensitively()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "starter", "Growth", 1));

        (await _catalogue.ListPlansAsync(
                tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None, "growth"))
            .Should()
            .BeEmpty();

        (await _catalogue.ListPlansAsync(
                tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None, "Growth"))
            .Should()
            .ContainSingle();
    }

    /// <summary>A family nobody authored is an empty list, not an error and not everything.</summary>
    [Fact]
    public async Task An_unknown_family_lists_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "starter", "growth", 1));

        (await _catalogue.ListPlansAsync(
                tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None, "nonexistent"))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// A blank family code reads as no family code at all, matching how the other listing
    /// parameters treat blank, so a caller forwarding an empty form field lists everything rather
    /// than nothing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_family_code_lists_every_family(string blank)
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "starter", "growth", 1));
        await Seed(tenantId, Plan(tenantId, "solo", family: null, rank: null));

        var listed = await _catalogue.ListPlansAsync(
            tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None, blank);

        listed.Should().HaveCount(2);
    }

    /// <summary>
    /// A plan with no family is never in one, so it is absent from every family listing.
    /// </summary>
    [Fact]
    public async Task A_plan_with_no_family_is_in_no_family()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "solo", family: null, rank: null));

        (await _catalogue.ListPlansAsync(
                tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None, "growth"))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// The family narrowing composes with the status filter rather than replacing it: asking for a
    /// family under the default Active view does not surface that family's archived plans.
    /// </summary>
    [Fact]
    public async Task A_family_listing_still_honours_the_status_filter()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(tenantId, Plan(tenantId, "starter", "growth", 1));
        await Seed(
            tenantId,
            Plan(tenantId, "retired", "growth", 2, status: CatalogueStatus.Archived));

        var active = await _catalogue.ListPlansAsync(
            tenantId, null, PlanCatalogueFilter.Active, CancellationToken.None, "growth");

        active.Select(plan => plan.Code).Should().Equal("starter");

        var all = await _catalogue.ListPlansAsync(
            tenantId, null, PlanCatalogueFilter.All, CancellationToken.None, "growth");

        all.Select(plan => plan.Code).Should().Equal("starter", "retired");

        var archived = await _catalogue.ListPlansAsync(
            tenantId, null, PlanCatalogueFilter.Archived, CancellationToken.None, "growth");

        archived.Select(plan => plan.Code).Should().Equal("retired");
    }

    /// <summary>A draft plan stays out of a family listing, as it stays out of every listing.</summary>
    [Fact]
    public async Task A_draft_plan_is_not_in_a_family_listing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Seed(
            tenantId,
            Plan(tenantId, "unreleased", "growth", 1, status: CatalogueStatus.Draft));

        (await _catalogue.ListPlansAsync(
                tenantId, null, PlanCatalogueFilter.All, CancellationToken.None, "growth"))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// Another tenant's family of the same name is not this tenant's.
    /// </summary>
    [Fact]
    public async Task A_family_listing_is_scoped_to_its_tenant()
    {
        var mine = MongoIntegrationFixture.NewTenantId();
        var theirs = MongoIntegrationFixture.NewTenantId();
        await Seed(mine, Plan(mine, "starter", "growth", 1));
        await Seed(theirs, Plan(theirs, "starter", "growth", 1));

        var listed = await _catalogue.ListPlansAsync(
            mine, null, PlanCatalogueFilter.Active, CancellationToken.None, "growth");

        listed.Should().ContainSingle().Which.TenantId.Should().Be(mine);
    }

    private Task Seed(string tenantId, Plan plan) =>
        _catalogue.TryCreatePlanAsync(plan, CancellationToken.None);

    private static Plan Plan(
        string tenantId,
        string code,
        string? family,
        int? rank,
        string? organizationId = null,
        CatalogueStatus status = CatalogueStatus.Active) => new()
    {
        TenantId = tenantId,
        // Composed so two tenants, and an organization's plan beside its tenant's, never collide on
        // the same id in the fixture's single shared database.
        ItemId = $"{tenantId}:{organizationId ?? "tenant"}:{code}",
        Code = code,
        DisplayName = code,
        FamilyCode = family,
        FamilyRank = rank,
        OrganizationId = organizationId,
        Status = status,
        Version = 1
    };
}
