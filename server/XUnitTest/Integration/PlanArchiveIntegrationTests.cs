using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// What archiving a plan actually does to the stored documents.
/// </summary>
/// <remarks>
/// The service-level tests for this feature mock the repository, so they prove which decision the
/// service reaches from a given answer and nothing at all about whether MongoDB gives that answer.
/// Everything here is a claim about the database rather than about our code: that a status check
/// and a version check happen in one write, that two real writers cannot both win, that a filter
/// returns the documents it says it does, and that an index exists with the filter it was declared
/// with. None of it can be demonstrated against a mock.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class PlanArchiveIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionCatalogueRepository _catalogue;

    public PlanArchiveIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _catalogue = new SubscriptionCatalogueRepository(fixture.DbContextProvider);
    }

    // ---- the write itself ---------------------------------------------------

    [Fact]
    public async Task Archiving_moves_the_plan_and_advances_its_version_in_one_write()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var plan = ActivePlan(tenantId);
        (await _catalogue.TryCreatePlanAsync(plan, CancellationToken.None)).Should().BeTrue();

        var archivedAt = DateTime.UtcNow;

        (await _catalogue.TryArchivePlanAsync(
                tenantId, plan.ItemId, plan.Version, archivedAt, CancellationToken.None))
            .Should().BeTrue();

        var stored = await _catalogue.GetPlanAsync(tenantId, plan.ItemId, CancellationToken.None);

        stored!.Status.Should().Be(CatalogueStatus.Archived);
        stored.Version.Should().Be(plan.Version + 1,
            "the version has to move, or a concurrent edit holding the old one would still win");
        stored.LastUpdatedDateUtc.Should().BeCloseTo(archivedAt, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// The version half of the filter. An edit landing in between must not be silently overwritten
    /// by an archive that was decided against terms nobody reviewed.
    /// </summary>
    [Fact]
    public async Task Archiving_against_a_stale_version_is_refused_by_the_database()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var plan = ActivePlan(tenantId);
        await _catalogue.TryCreatePlanAsync(plan, CancellationToken.None);

        var edited = ActivePlan(tenantId);
        edited.DisplayName = "Renamed by somebody else";
        (await _catalogue.TryUpdatePlanAsync(
                tenantId, plan.ItemId, plan.Version, edited, CancellationToken.None))
            .Should().BeTrue();

        (await _catalogue.TryArchivePlanAsync(
                tenantId, plan.ItemId, plan.Version, DateTime.UtcNow, CancellationToken.None))
            .Should().BeFalse("the version the archive was decided against is no longer current");

        var stored = await _catalogue.GetPlanAsync(tenantId, plan.ItemId, CancellationToken.None);

        stored!.Status.Should().Be(CatalogueStatus.Active, "the refused archive must not have applied");
    }

    /// <summary>
    /// The status half of the filter, and the reason a repeat is reported as a no-op rather than as
    /// a second successful write.
    /// </summary>
    [Fact]
    public async Task Archiving_an_already_archived_plan_writes_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var plan = ActivePlan(tenantId);
        await _catalogue.TryCreatePlanAsync(plan, CancellationToken.None);

        await _catalogue.TryArchivePlanAsync(
            tenantId, plan.ItemId, plan.Version, DateTime.UtcNow, CancellationToken.None);

        var afterFirst = await _catalogue.GetPlanAsync(tenantId, plan.ItemId, CancellationToken.None);

        (await _catalogue.TryArchivePlanAsync(
                tenantId, plan.ItemId, afterFirst!.Version, DateTime.UtcNow, CancellationToken.None))
            .Should().BeFalse();

        var afterSecond = await _catalogue.GetPlanAsync(tenantId, plan.ItemId, CancellationToken.None);

        afterSecond!.Version.Should().Be(afterFirst.Version,
            "an already-archived plan must not be written again, or every retry would bump the version");
    }

    /// <summary>
    /// A draft was never on a menu, so there is nothing to take off one, and archiving is
    /// permanent. Enforced by the filter rather than by the service alone.
    /// </summary>
    [Fact]
    public async Task A_draft_plan_is_refused_by_the_filter_itself()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var plan = ActivePlan(tenantId);
        plan.Status = CatalogueStatus.Draft;
        await _catalogue.TryCreatePlanAsync(plan, CancellationToken.None);

        (await _catalogue.TryArchivePlanAsync(
                tenantId, plan.ItemId, plan.Version, DateTime.UtcNow, CancellationToken.None))
            .Should().BeFalse();

        var stored = await _catalogue.GetPlanAsync(tenantId, plan.ItemId, CancellationToken.None);

        stored!.Status.Should().Be(CatalogueStatus.Draft);
    }

    /// <summary>
    /// Two real writers, started together. Exactly one may win, and the plan must end at one
    /// version past where it started rather than two.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_archives_produce_one_winner_and_one_version_increment()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var plan = ActivePlan(tenantId);
        await _catalogue.TryCreatePlanAsync(plan, CancellationToken.None);

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                _catalogue.TryArchivePlanAsync(
                    tenantId, plan.ItemId, plan.Version, DateTime.UtcNow, CancellationToken.None)));

        attempts.Count(won => won).Should().Be(1,
            "the status and version filter is the whole guarantee: a second winner means two " +
            "writers both believed they archived it");

        var stored = await _catalogue.GetPlanAsync(tenantId, plan.ItemId, CancellationToken.None);

        stored!.Status.Should().Be(CatalogueStatus.Archived);
        stored.Version.Should().Be(plan.Version + 1);
    }

    // ---- the fallback archiving must not break ------------------------------

    /// <summary>
    /// The regression this feature could most easily have introduced, and the reason the archived
    /// lookup is a second call rather than a widened first one: an organization's archived plan
    /// must not shadow the tenant's active plan of the same code and refuse a sale that should
    /// have gone through.
    /// </summary>
    [Fact]
    public async Task An_archived_organization_plan_does_not_hide_the_active_tenant_plan()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var tenantWide = ActivePlan(tenantId);
        tenantWide.OrganizationId = null;
        await _catalogue.TryCreatePlanAsync(tenantWide, CancellationToken.None);

        var organizationOwned = ActivePlan(tenantId);
        organizationOwned.OrganizationId = "org-1";
        await _catalogue.TryCreatePlanAsync(organizationOwned, CancellationToken.None);

        // While both are active, the organization's own plan is what resolves.
        var beforeArchiving = await _catalogue.FindPlanByCodeAsync(
            tenantId, "org-1", "professional", CancellationToken.None);

        beforeArchiving!.ItemId.Should().Be(organizationOwned.ItemId);

        await _catalogue.TryArchivePlanAsync(
            tenantId, organizationOwned.ItemId, organizationOwned.Version, DateTime.UtcNow,
            CancellationToken.None);

        var afterArchiving = await _catalogue.FindPlanByCodeAsync(
            tenantId, "org-1", "professional", CancellationToken.None);

        afterArchiving.Should().NotBeNull(
            "the tenant's plan is still on sale, and archiving somebody else's must not have " +
            "taken it off the menu");
        afterArchiving!.ItemId.Should().Be(tenantWide.ItemId);
    }

    /// <summary>
    /// The archived lookup follows the same visibility as the active one, so a better error message
    /// never becomes a way to discover that a code exists in an organization the caller cannot see.
    /// </summary>
    [Fact]
    public async Task The_archived_lookup_stays_within_what_the_caller_can_see()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var otherOrganizations = ActivePlan(tenantId);
        otherOrganizations.OrganizationId = "org-9";
        await _catalogue.TryCreatePlanAsync(otherOrganizations, CancellationToken.None);
        await _catalogue.TryArchivePlanAsync(
            tenantId, otherOrganizations.ItemId, otherOrganizations.Version, DateTime.UtcNow,
            CancellationToken.None);

        var found = await _catalogue.FindArchivedPlanByCodeAsync(
            tenantId, "org-1", "professional", CancellationToken.None);

        found.Should().BeNull(
            "org-1 cannot see org-9's plan, so the refusal it receives must stay a plain not-found");
    }

    [Fact]
    public async Task The_archived_lookup_finds_a_plan_the_caller_can_see()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var plan = ActivePlan(tenantId);
        plan.OrganizationId = null;
        await _catalogue.TryCreatePlanAsync(plan, CancellationToken.None);
        await _catalogue.TryArchivePlanAsync(
            tenantId, plan.ItemId, plan.Version, DateTime.UtcNow, CancellationToken.None);

        var found = await _catalogue.FindArchivedPlanByCodeAsync(
            tenantId, "org-1", "professional", CancellationToken.None);

        found!.ItemId.Should().Be(plan.ItemId);
    }

    // ---- listing ------------------------------------------------------------

    /// <summary>
    /// The three filters against stored documents, including the part that differs between them:
    /// Active collapses by code, and the archived views must not.
    /// </summary>
    [Fact]
    public async Task The_three_filters_return_what_they_say_they_do()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var tenantWide = ActivePlan(tenantId);
        tenantWide.OrganizationId = null;
        await _catalogue.TryCreatePlanAsync(tenantWide, CancellationToken.None);

        var organizationOwned = ActivePlan(tenantId);
        organizationOwned.OrganizationId = "org-1";
        await _catalogue.TryCreatePlanAsync(organizationOwned, CancellationToken.None);

        var retired = ActivePlan(tenantId);
        retired.OrganizationId = "org-1";
        retired.Code = "legacy";
        await _catalogue.TryCreatePlanAsync(retired, CancellationToken.None);
        await _catalogue.TryArchivePlanAsync(
            tenantId, retired.ItemId, retired.Version, DateTime.UtcNow, CancellationToken.None);

        var draft = ActivePlan(tenantId);
        draft.OrganizationId = "org-1";
        draft.Code = "unfinished";
        draft.Status = CatalogueStatus.Draft;
        await _catalogue.TryCreatePlanAsync(draft, CancellationToken.None);

        var active = await _catalogue.ListPlansAsync(
            tenantId, "org-1", PlanCatalogueFilter.Active, CancellationToken.None);
        var archived = await _catalogue.ListPlansAsync(
            tenantId, "org-1", PlanCatalogueFilter.Archived, CancellationToken.None);
        var all = await _catalogue.ListPlansAsync(
            tenantId, "org-1", PlanCatalogueFilter.All, CancellationToken.None);

        // Collapsed: the organization's own "professional" hides the tenant's, matching what
        // FindPlanByCodeAsync resolves.
        active.Select(plan => plan.ItemId).Should().BeEquivalentTo([organizationOwned.ItemId]);

        archived.Select(plan => plan.ItemId).Should().BeEquivalentTo([retired.ItemId]);

        all.Select(plan => plan.ItemId).Should().BeEquivalentTo(
            [organizationOwned.ItemId, retired.ItemId]);

        // Draft is in none of them, which is what it has always been to every catalogue view.
        all.Should().NotContain(plan => plan.ItemId == draft.ItemId);
    }

    /// <summary>
    /// History is not collapsed. Two archived plans sharing a code are two records, and the
    /// replacement sharing that code is usually why somebody is reading them.
    /// </summary>
    [Fact]
    public async Task Archived_plans_sharing_a_code_are_all_returned()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = ActivePlan(tenantId);
        first.OrganizationId = null;
        await _catalogue.TryCreatePlanAsync(first, CancellationToken.None);
        await _catalogue.TryArchivePlanAsync(
            tenantId, first.ItemId, first.Version, DateTime.UtcNow, CancellationToken.None);

        var second = ActivePlan(tenantId);
        second.OrganizationId = "org-1";
        await _catalogue.TryCreatePlanAsync(second, CancellationToken.None);
        await _catalogue.TryArchivePlanAsync(
            tenantId, second.ItemId, second.Version, DateTime.UtcNow, CancellationToken.None);

        var archived = await _catalogue.ListPlansAsync(
            tenantId, "org-1", PlanCatalogueFilter.Archived, CancellationToken.None);

        archived.Select(plan => plan.ItemId).Should().BeEquivalentTo([first.ItemId, second.ItemId]);
    }

    /// <summary>
    /// Under All, an archived plan must not collapse an active one of the same code either: what is
    /// on sale would then be misreported as retired.
    /// </summary>
    [Fact]
    public async Task An_archived_plan_never_hides_an_active_plan_of_the_same_code()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var live = ActivePlan(tenantId);
        live.OrganizationId = null;
        await _catalogue.TryCreatePlanAsync(live, CancellationToken.None);

        var retired = ActivePlan(tenantId);
        retired.OrganizationId = "org-1";
        await _catalogue.TryCreatePlanAsync(retired, CancellationToken.None);
        await _catalogue.TryArchivePlanAsync(
            tenantId, retired.ItemId, retired.Version, DateTime.UtcNow, CancellationToken.None);

        var all = await _catalogue.ListPlansAsync(
            tenantId, "org-1", PlanCatalogueFilter.All, CancellationToken.None);

        all.Should().Contain(plan => plan.ItemId == live.ItemId);
        all.Should().Contain(plan => plan.ItemId == retired.ItemId);
    }

    // ---- the audit index ----------------------------------------------------

    /// <summary>
    /// The index is declared partial rather than sparse, and the difference is not cosmetic: a
    /// sparse compound index includes a document when <em>any</em> indexed field exists, and
    /// TenantId and OrganizationId exist on every audit event ever written. Declared sparse, it
    /// would have indexed the whole collection while reading as though it excluded it.
    /// </summary>
    [Fact]
    public async Task The_aggregate_audit_index_is_partial_on_the_aggregate_fields()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var audit = new SubscriptionAuditRepository(_fixture.DbContextProvider);

        await audit.AppendAsync(
            new SubscriptionAuditEvent
            {
                TenantId = tenantId,
                OrganizationId = "org-1",
                AggregateType = "Plan",
                AggregateId = "plan-1",
                AggregateCode = "professional",
                Operation = "PlanArchive",
                Outcome = "Changed"
            },
            CancellationToken.None);

        var indexes = await _fixture.Database
            .GetCollection<BsonDocument>($"{tenantId}_SubscriptionAuditEvents")
            .Indexes.List()
            .ToListAsync();

        var aggregate = indexes.Find(index =>
            index.GetValue("name", "").AsString == "ix_subscription_audit_aggregate");

        aggregate.Should().NotBeNull("the index the plan-history query depends on must exist");

        aggregate!.Contains("sparse").Should().BeFalse(
            "sparse would not have excluded a single existing subscription event");

        var filter = aggregate.GetValue("partialFilterExpression", null)?.AsBsonDocument;

        filter.Should().NotBeNull("without the filter the index holds every audit event ever written");
        filter!.Names.Should().Contain("AggregateType");
        filter.Names.Should().Contain("AggregateId");
    }

    private static Plan ActivePlan(string tenantId) => new()
    {
        TenantId = tenantId,
        Code = "professional",
        DisplayName = "Professional",
        Status = CatalogueStatus.Active,
        Version = 1
    };
}
