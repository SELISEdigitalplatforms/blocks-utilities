using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription;

public sealed class PlanDefinitionRequestValidatorTests
{
    [Fact]
    public async Task A_lifetime_capacity_without_overage_is_valid()
    {
        var request = RequestWithLifetimeMeter();

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_lifetime_capacity_cannot_promise_monthly_overage_billing()
    {
        var request = RequestWithLifetimeMeter();
        request.Meters[0].OverageAllowed = true;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_lifetime_meter_overage_invalid");
    }

    [Fact]
    public async Task A_lifetime_capacity_cannot_have_a_separate_trial_allowance()
    {
        var request = RequestWithLifetimeMeter();
        request.TrialGrants = [new TrialGrantRequest { MeterKey = "storage", IncludedQuantity = 1_000 }];

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_lifetime_meter_trial_grant_invalid");
    }

    private static UpdatePlanRequest RequestWithLifetimeMeter() => new()
    {
        DisplayName = "Storage plan",
        Meters =
        [
            new PlanMeterRequest
            {
                MeterKey = "storage",
                DisplayName = "Storage",
                UnitLabel = "byte",
                IncludedQuantity = 5_368_709_120,
                ResetPolicy = MeterResetPolicy.Never,
                OverageAllowed = false
            }
        ]
    };
}
