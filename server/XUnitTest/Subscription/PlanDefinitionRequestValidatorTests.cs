using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Utilities;
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

    // ------------------------------------------------------------------ fractional quantities

    /// <summary>
    /// A meter that declares no scale counts whole units, and a fractional allowance on it is
    /// refused. This is the state of every plan authored before fractions existed.
    /// </summary>
    [Fact]
    public async Task A_fraction_on_a_whole_unit_meter_is_refused()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].IncludedQuantity = 512.5m;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_meter_quantity_scale_exceeded");
    }

    [Fact]
    public async Task A_fraction_within_the_declared_scale_is_accepted()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].QuantityScale = 1;
        request.Meters[0].IncludedQuantity = 512.5m;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_quantity_finer_than_the_declared_scale_is_refused()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].QuantityScale = 2;
        request.Meters[0].IncludedQuantity = 512.005m;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_meter_quantity_scale_exceeded");
    }

    [Fact]
    public async Task A_scale_beyond_the_platform_maximum_is_refused()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].QuantityScale = MeterQuantity.MaxScale + 1;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_meter_quantity_scale_invalid");
    }

    /// <summary>
    /// An invalid scale reports one mistake, not two: the scale rule owns it, and the conformance
    /// rule stands down rather than also complaining that nothing fits a scale that is not a scale.
    /// </summary>
    [Fact]
    public async Task An_invalid_scale_is_not_reported_twice()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].QuantityScale = -1;
        request.Meters[0].IncludedQuantity = 512.5m;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().NotContain(error =>
            error.ErrorCode == "subscription_meter_quantity_scale_exceeded");
    }

    /// <summary>
    /// A rate band's bound has to be a quantity the meter can hold, or the band's edge would sit
    /// between two representable quantities.
    /// </summary>
    [Fact]
    public async Task A_tier_bound_finer_than_the_meters_scale_is_refused()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].QuantityScale = 1;
        request.Meters[0].RateTables =
        [
            new MeterRateTableRequest
            {
                CurrencyCode = "CHF",
                Tiers = [new MeterTierRequest { UpToQuantity = 400.05m, UnitAmountMinor = 10 }]
            }
        ];

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_meter_quantity_scale_exceeded");
    }

    [Fact]
    public async Task A_carry_forward_cap_finer_than_the_meters_scale_is_refused()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].QuantityScale = 1;
        request.Meters[0].ResetPolicy = MeterResetPolicy.CarryForward;
        request.Meters[0].CarryForwardCap = 50.05m;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_meter_quantity_scale_exceeded");
    }

    /// <summary>
    /// A trial grant replaces its meter's allowance, so it is held to that meter's scale — not to
    /// the plan's finest, and not to none at all.
    /// </summary>
    [Fact]
    public async Task A_trial_grant_finer_than_its_own_meters_scale_is_refused()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].QuantityScale = 1;
        request.TrialGrants =
        [
            new TrialGrantRequest { MeterKey = "screening", IncludedQuantity = 25.25m }
        ];

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_trial_grant_quantity_scale_exceeded");
    }

    [Fact]
    public async Task A_trial_grant_within_its_meters_scale_is_accepted()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].QuantityScale = 2;
        request.TrialGrants =
        [
            new TrialGrantRequest { MeterKey = "screening", IncludedQuantity = 25.25m }
        ];

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// A quantity too large to hold is refused at authoring time, which is the only moment there
    /// is a person to tell. Decimal128 can carry more than a decimal can read back.
    /// </summary>
    [Fact]
    public async Task A_quantity_beyond_the_representable_range_is_refused()
    {
        var request = RequestWithPeriodicMeter();
        request.Meters[0].IncludedQuantity = MeterQuantity.MaxMagnitude + 1;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_meter_quantity_scale_exceeded");
    }

    private static UpdatePlanRequest RequestWithPeriodicMeter() => new()
    {
        DisplayName = "Screening plan",
        Meters =
        [
            new PlanMeterRequest
            {
                MeterKey = "screening",
                DisplayName = "Screenings",
                UnitLabel = "screening",
                IncludedQuantity = 100,
                ResetPolicy = MeterResetPolicy.Periodic,
                OverageAllowed = false
            }
        ]
    };

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
