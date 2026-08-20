using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// What a plan looks like to whoever authored it.
/// </summary>
/// <remarks>
/// The console configures a plan and then has to read it back to show what it built. Anything
/// authorable that the response drops is invisible from that moment on — a field the portal
/// offers to set and then never displays again reads as data loss, whether or not it was stored.
/// </remarks>
public sealed class PlanResponseMapperTests
{
    private readonly PlanResponseMapper _mapper = new();

    [Fact]
    public void An_organization_scoped_plan_reports_its_organization()
    {
        var response = _mapper.ToResponse(Plan("organization-1"), []);

        response.OrganizationId.Should().Be("organization-1",
            "a caller seeing plans from several organizations cannot otherwise tell them apart");
    }

    [Fact]
    public void A_tenant_wide_plan_reports_no_organization()
    {
        var response = _mapper.ToResponse(Plan(organizationId: null), []);

        response.OrganizationId.Should().BeNull();
    }

    [Fact]
    public void A_meter_carries_the_thresholds_it_will_notify_on()
    {
        var response = _mapper.ToResponse(Plan("organization-1"), []);

        response.Meters[0].ThresholdPercents.Should().Equal(80, 100);
    }

    [Fact]
    public void A_meter_reports_its_aggregation_by_name()
    {
        var response = _mapper.ToResponse(Plan("organization-1"), []);

        response.Meters[0].Aggregation.Should().Be(nameof(MeterAggregation.Sum),
            "a client that has to know 0 means summed is coupled to our storage format");
    }

    [Fact]
    public void A_meter_reports_its_reset_policy_by_name()
    {
        var plan = Plan("organization-1");
        plan.Meters[0].ResetPolicy = MeterResetPolicy.Never;

        var response = _mapper.ToResponse(plan, []);

        response.Meters[0].ResetPolicy.Should().Be(nameof(MeterResetPolicy.Never));
    }

    /// <summary>
    /// Overage that cannot be priced is charged nothing, so an author has to be able to see
    /// whether a meter permitting overage actually has a table behind it.
    /// </summary>
    [Fact]
    public void A_meter_carries_the_tiers_its_overage_is_priced_against()
    {
        var response = _mapper.ToResponse(Plan("organization-1"), []);

        var table = response.Meters[0].RateTables.Should().ContainSingle().Subject;
        table.CurrencyCode.Should().Be("CHF");
        table.Tiers.Should().HaveCount(2);
        table.Tiers[0].UpToQuantity.Should().Be(1_000);
        table.Tiers[0].UnitAmountMinor.Should().Be(5);
        table.Tiers[1].UpToQuantity.Should().BeNull("the last band is the unbounded one");
        table.Tiers[1].UnitAmountMinor.Should().Be(3);
    }

    /// <summary>
    /// Copied rather than shared, so a caller mutating the list it was handed cannot reach back
    /// into the stored plan.
    /// </summary>
    [Fact]
    public void A_meters_thresholds_are_not_the_stored_list()
    {
        var plan = Plan("organization-1");

        var response = _mapper.ToResponse(plan, []);
        response.Meters[0].ThresholdPercents.Add(999);

        plan.Meters[0].ThresholdPercents.Should().Equal(80, 100);
    }

    private static Plan Plan(string? organizationId) => new()
    {
        ItemId = "plan-1",
        TenantId = "tenant-1",
        OrganizationId = organizationId,
        Code = "professional",
        DisplayName = "Professional",
        Meters =
        [
            new PlanMeter
            {
                MeterKey = "screening",
                DisplayName = "Screenings",
                UnitLabel = "screening",
                Aggregation = MeterAggregation.Sum,
                IncludedQuantity = 500,
                OverageAllowed = true,
                ThresholdPercents = [80, 100],
                RateTables =
                [
                    new MeterRateTable
                    {
                        CurrencyCode = "CHF",
                        Tiers =
                        [
                            new MeterTier { UpToQuantity = 1_000, UnitAmountMinor = 5 },
                            new MeterTier { UpToQuantity = null, UnitAmountMinor = 3 }
                        ]
                    }
                ]
            }
        ]
    };
}
