using System.Text.Json;
using FluentValidation;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class CreatePlanRequestValidator : AbstractValidator<CreatePlanRequest>
{
    private const int MaximumCodeLength = 64;
    private const int MaximumFeaturesLength = 16_384;

    public CreatePlanRequestValidator()
    {
        RuleFor(request => request.Code)
            .NotEmpty()
            .MaximumLength(MaximumCodeLength)
            .Matches("^[a-z0-9_-]+$")
            .WithMessage(
                "A plan code may contain only lowercase letters, digits, hyphens and underscores.")
            .WithErrorCode("subscription_plan_code_invalid");

        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(200);

        RuleFor(request => request.TrialDays)
            .InclusiveBetween(1, 365)
            .When(request => request.TrialDays.HasValue);

        RuleFor(request => request.FeaturesJson)
            .Must(BeAJsonObject)
            .When(request => !string.IsNullOrWhiteSpace(request.FeaturesJson))
            .WithMessage(
                "Plan features must be a JSON object. It is stored verbatim and returned to " +
                "callers, so it has to be something they can parse.")
            .WithErrorCode("subscription_plan_features_invalid");

        RuleForEach(request => request.QuantityItems)
            .ChildRules(item =>
            {
                item.RuleFor(quantity => quantity.ItemKey).NotEmpty().MaximumLength(64);
                item.RuleFor(quantity => quantity.UnitLabel).NotEmpty().MaximumLength(64);
                item.RuleFor(quantity => quantity.MinQuantity).GreaterThanOrEqualTo(0);
                item.RuleFor(quantity => quantity)
                    .Must(quantity =>
                        quantity.MaxQuantity is null ||
                        quantity.MaxQuantity >= quantity.MinQuantity)
                    .WithName(nameof(PlanQuantityItemRequest.MaxQuantity))
                    .WithMessage("A maximum quantity cannot be below the minimum.");
            });

        RuleForEach(request => request.Meters)
            .ChildRules(meter =>
            {
                meter.RuleFor(definition => definition.MeterKey).NotEmpty().MaximumLength(64);
                meter.RuleFor(definition => definition.UnitLabel).NotEmpty().MaximumLength(64);
                meter.RuleFor(definition => definition.IncludedQuantity)
                    .GreaterThanOrEqualTo(0);
                meter.RuleForEach(definition => definition.ThresholdPercents)
                    .InclusiveBetween(1, 100);
                meter.RuleFor(definition => definition.RateTables)
                    .Must(HaveWellOrderedTiers)
                    .WithMessage(
                        "Rate tiers must ascend, and only the last may be unbounded — " +
                        "otherwise a quantity falls into two bands and the bill depends on " +
                        "which is read first.")
                    .WithErrorCode("subscription_meter_tiers_invalid");
            });

        RuleForEach(request => request.Entitlements)
            .ChildRules(entitlement =>
            {
                entitlement.RuleFor(definition => definition.Key).NotEmpty().MaximumLength(64);
                entitlement.RuleFor(definition => definition)
                    .Must(definition =>
                        definition.LimitKind != EntitlementLimitKind.Count ||
                        (definition.Limit.HasValue &&
                         !string.IsNullOrWhiteSpace(definition.MeterKey)))
                    .WithName(nameof(PlanEntitlementRequest.Limit))
                    .WithMessage(
                        "A counted entitlement needs both a limit and the meter that draws it down.");
            });

        RuleFor(request => request)
            .Must(EveryEntitlementMeterExists)
            .WithName(nameof(CreatePlanRequest.Entitlements))
            .WithMessage(
                "An entitlement names a meter the plan does not define, so nothing would ever " +
                "draw it down.")
            .WithErrorCode("subscription_entitlement_meter_unknown");

        RuleFor(request => request)
            .Must(EveryTrialGrantMeterExists)
            .WithName(nameof(CreatePlanRequest.TrialGrants))
            .WithMessage("A trial grant names a meter the plan does not define.")
            .WithErrorCode("subscription_trial_grant_meter_unknown");
    }

    private static bool BeAJsonObject(string? featuresJson)
    {
        if (featuresJson is null || featuresJson.Length > MaximumFeaturesLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(featuresJson);

            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HaveWellOrderedTiers(List<MeterRateTableRequest> rateTables) =>
        rateTables.TrueForAll(table =>
        {
            var tiers = table.Tiers;

            if (tiers.Count == 0)
            {
                return true;
            }

            // Only the final tier may be open-ended; an unbounded band in the middle would
            // swallow every band after it.
            if (tiers.Take(tiers.Count - 1).Any(tier => tier.UpToQuantity is null))
            {
                return false;
            }

            // Every bound, including the last when it is closed — checking only the leading
            // ones lets a final band sit below its predecessor and swallow the tier before it.
            var bounds = tiers
                .Where(tier => tier.UpToQuantity.HasValue)
                .Select(tier => tier.UpToQuantity!.Value)
                .ToArray();

            return Array.TrueForAll(bounds, bound => bound > 0) &&
                   bounds.Zip(bounds.Skip(1)).All(pair => pair.Second > pair.First);
        });

    private static bool EveryEntitlementMeterExists(CreatePlanRequest request)
    {
        var meterKeys = request.Meters
            .Select(meter => meter.MeterKey)
            .ToHashSet(StringComparer.Ordinal);

        return request.Entitlements.TrueForAll(entitlement =>
            string.IsNullOrWhiteSpace(entitlement.MeterKey) ||
            meterKeys.Contains(entitlement.MeterKey));
    }

    private static bool EveryTrialGrantMeterExists(CreatePlanRequest request)
    {
        var meterKeys = request.Meters
            .Select(meter => meter.MeterKey)
            .ToHashSet(StringComparer.Ordinal);

        return request.TrialGrants.TrueForAll(grant => meterKeys.Contains(grant.MeterKey));
    }
}
