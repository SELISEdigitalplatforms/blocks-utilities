using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// What an edit actually writes to the stored plan document.
/// </summary>
/// <remarks>
/// <see cref="SubscriptionCatalogueRepository.TryUpdatePlanAsync"/> builds its Mongo update from
/// an explicit list of fields rather than replacing the whole document, so a field the service
/// layer populates but the repository forgets to list is silently never persisted — a mocked
/// repository in a service-level test cannot catch that; only the real update against a real
/// collection can. This guards exactly the field this class was introduced for: a plan's trial
/// duration kind and count previously fell into that gap, so an edit that changed a plan's trial
/// rule reported success while quietly keeping the old one.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionCatalogueRepositoryIntegrationTests
{
    private readonly SubscriptionCatalogueRepository _catalogue;

    public SubscriptionCatalogueRepositoryIntegrationTests(MongoIntegrationFixture fixture) =>
        _catalogue = new SubscriptionCatalogueRepository(fixture.DbContextProvider);

    [Fact]
    public async Task Editing_a_plan_persists_its_new_trial_duration_kind_and_count()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var plan = NewPlan(tenantId);
        plan.TrialDurationKind = TrialDurationKind.Days;
        plan.TrialDurationCount = 14;

        (await _catalogue.TryCreatePlanAsync(plan, CancellationToken.None)).Should().BeTrue();

        var edited = NewPlan(tenantId);
        edited.TrialDurationKind = TrialDurationKind.AnniversaryMonths;
        edited.TrialDurationCount = 2;

        (await _catalogue.TryUpdatePlanAsync(
                tenantId, plan.ItemId, plan.Version, edited, CancellationToken.None))
            .Should().BeTrue();

        var stored = await _catalogue.GetPlanAsync(tenantId, plan.ItemId, CancellationToken.None);

        stored!.TrialDurationKind.Should().Be(TrialDurationKind.AnniversaryMonths,
            "the edit changed the duration kind, and reopening the plan must show what was saved, " +
            "not what was there before");
        stored.TrialDurationCount.Should().Be(2);
    }

    [Fact]
    public async Task Editing_a_legacy_day_based_plan_to_end_of_calendar_month_drops_the_old_count()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var plan = NewPlan(tenantId);
        plan.TrialDays = 14;

        (await _catalogue.TryCreatePlanAsync(plan, CancellationToken.None)).Should().BeTrue();

        var edited = NewPlan(tenantId);
        edited.TrialDays = null;
        edited.TrialDurationKind = TrialDurationKind.EndOfCalendarMonth;
        edited.TrialDurationCount = null;

        (await _catalogue.TryUpdatePlanAsync(
                tenantId, plan.ItemId, plan.Version, edited, CancellationToken.None))
            .Should().BeTrue();

        var stored = await _catalogue.GetPlanAsync(tenantId, plan.ItemId, CancellationToken.None);

        stored!.TrialDurationKind.Should().Be(TrialDurationKind.EndOfCalendarMonth);
        stored.TrialDays.Should().BeNull(
            "the legacy field must not survive an edit that moved the plan onto a current-style rule");
    }

    private static Plan NewPlan(string tenantId) => new()
    {
        TenantId = tenantId,
        Code = "professional",
        DisplayName = "Professional",
        Status = CatalogueStatus.Active,
        Version = 1
    };
}
