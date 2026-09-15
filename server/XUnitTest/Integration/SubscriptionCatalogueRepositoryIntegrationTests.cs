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

    /// <summary>
    /// The array-filter update <see cref="SubscriptionCatalogueRepository.TryUpdatePlanMeterRatesAsync"/>
    /// builds is the one part of that method a mocked repository cannot exercise: whether Mongo's
    /// positional filtered identifier actually matches the meter this call named, and whether it
    /// leaves every other meter's rate tables alone.
    /// </summary>
    [Fact]
    public async Task Updating_one_meters_rates_leaves_every_other_meter_and_field_untouched()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var plan = NewPlan(tenantId);
        plan.Meters =
        [
            new PlanMeter
            {
                MeterKey = "screenings",
                DisplayName = "Screenings",
                RateTables = [new MeterRateTable
                {
                    CurrencyCode = "USD",
                    Tiers = [new MeterTier { UnitAmountMinor = 10 }]
                }]
            },
            new PlanMeter { MeterKey = "storage", DisplayName = "Storage" }
        ];

        (await _catalogue.TryCreatePlanAsync(plan, CancellationToken.None)).Should().BeTrue();

        var newRates = new List<MeterRateTable>
        {
            new()
            {
                CurrencyCode = "EUR",
                Tiers =
                [
                    new MeterTier { UpToQuantity = 100, UnitAmountMinor = 20 },
                    new MeterTier { UnitAmountMinor = 15 }
                ]
            }
        };

        (await _catalogue.TryUpdatePlanMeterRatesAsync(
                tenantId, plan.ItemId, "screenings", plan.Version, newRates, DateTime.UtcNow,
                CancellationToken.None))
            .Should().BeTrue();

        var stored = await _catalogue.GetPlanAsync(tenantId, plan.ItemId, CancellationToken.None);

        var screenings = stored!.Meters.Find(meter => meter.MeterKey == "screenings")!;
        screenings.RateTables.Should().BeEquivalentTo(newRates,
            "the named meter's rate tables must be replaced with exactly what was sent");
        screenings.DisplayName.Should().Be("Screenings",
            "the update touches only RateTables — the rest of the meter must survive unedited");

        var storage = stored.Meters.Find(meter => meter.MeterKey == "storage")!;
        storage.RateTables.Should().BeEmpty(
            "the array filter must match only the named meter, or a sibling meter would gain " +
            "rates nobody set on it");

        stored.Version.Should().Be(plan.Version + 1,
            "every catalogue write bumps the version a future snapshot is captured under");
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
