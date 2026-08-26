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

    [Fact]
    public async Task Legacy_trial_days_alone_is_valid()
    {
        var request = new UpdatePlanRequest { DisplayName = "Plan", TrialDays = 14 };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_trial_days_together_with_the_new_duration_fields_is_rejected()
    {
        var request = new UpdatePlanRequest
        {
            DisplayName = "Plan",
            TrialDays = 14,
            TrialDurationKind = TrialDurationKind.Days,
            TrialDurationCount = 14
        };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_trial_duration_fields_conflict");
    }

    [Fact]
    public async Task A_days_mode_trial_requires_a_count_between_1_and_365()
    {
        var request = new UpdatePlanRequest
        {
            DisplayName = "Plan",
            TrialDurationKind = TrialDurationKind.Days
        };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();

        request.TrialDurationCount = 366;
        result = await new PlanDefinitionRequestValidator().ValidateAsync(request);
        result.IsValid.Should().BeFalse();

        request.TrialDurationCount = 14;
        result = await new PlanDefinitionRequestValidator().ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task An_anniversary_months_trial_requires_a_count_between_1_and_12()
    {
        var request = new UpdatePlanRequest
        {
            DisplayName = "Plan",
            TrialDurationKind = TrialDurationKind.AnniversaryMonths
        };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);
        result.IsValid.Should().BeFalse();

        request.TrialDurationCount = 13;
        result = await new PlanDefinitionRequestValidator().ValidateAsync(request);
        result.IsValid.Should().BeFalse();

        request.TrialDurationCount = 12;
        result = await new PlanDefinitionRequestValidator().ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task An_end_of_calendar_month_trial_must_not_specify_a_count()
    {
        var request = new UpdatePlanRequest
        {
            DisplayName = "Plan",
            TrialDurationKind = TrialDurationKind.EndOfCalendarMonth,
            TrialDurationCount = 1
        };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();

        request.TrialDurationCount = null;
        result = await new PlanDefinitionRequestValidator().ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_duration_count_without_a_duration_kind_is_rejected()
    {
        var request = new UpdatePlanRequest { DisplayName = "Plan", TrialDurationCount = 14 };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task No_trial_fields_at_all_is_valid()
    {
        var request = new UpdatePlanRequest { DisplayName = "Plan" };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
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
