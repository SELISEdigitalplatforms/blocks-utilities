using System.Text.Json;
using FluentValidation;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

/// <summary>
/// Everything a plan's own contents have to satisfy, whether it is being created or edited.
/// </summary>
/// <remarks>
/// Its own validator rather than two copies: an edit that could store something a create would
/// have rejected is a hole, and the only way to be sure the two agree is for there to be one
/// rule.
/// </remarks>
public sealed class PlanDefinitionRequestValidator : AbstractValidator<PlanDefinitionRequest>
{
    private const int MaximumFeaturesLength = 16_384;

    public PlanDefinitionRequestValidator()
    {
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(200);

        RuleFor(request => request.TrialDays)
            .InclusiveBetween(1, 365)
            .When(request => request.TrialDays.HasValue);

        RuleFor(request => request.UsageIntervalCount).InclusiveBetween(1, 100);
        RuleFor(request => request.FamilyCode).MaximumLength(64);
        RuleFor(request => request.FamilyRank).GreaterThanOrEqualTo(0)
            .When(request => request.FamilyRank.HasValue);
        RuleFor(request => request)
            .Must(request => string.IsNullOrWhiteSpace(request.FamilyCode) == !request.FamilyRank.HasValue)
            .WithMessage("Family code and family rank must be supplied together.");

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
                meter.RuleFor(definition => definition.ResetPolicy).IsInEnum();
                meter.RuleFor(definition => definition)
                    .Must(definition =>
                        definition.ResetPolicy != MeterResetPolicy.Never ||
                        (!definition.OverageAllowed && definition.RateTables.Count == 0))
                    .WithMessage(
                        "A never-reset meter is persistent capacity: block at its allowance " +
                        "instead of configuring periodic overage billing.")
                    .WithErrorCode("subscription_lifetime_meter_overage_invalid");
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
            .WithName(nameof(PlanDefinitionRequest.Entitlements))
            .WithMessage(
                "An entitlement names a meter the plan does not define, so nothing would ever " +
                "draw it down.")
            .WithErrorCode("subscription_entitlement_meter_unknown");

        RuleFor(request => request)
            .Must(EveryTrialGrantMeterExists)
            .WithName(nameof(PlanDefinitionRequest.TrialGrants))
            .WithMessage("A trial grant names a meter the plan does not define.")
            .WithErrorCode("subscription_trial_grant_meter_unknown");

        RuleFor(request => request)
            .Must(request => request.TrialGrants.All(grant =>
                request.Meters.Any(meter =>
                    string.Equals(meter.MeterKey, grant.MeterKey, StringComparison.Ordinal) &&
                    meter.ResetPolicy == MeterResetPolicy.Periodic)))
            .WithName(nameof(PlanDefinitionRequest.TrialGrants))
            .WithMessage("Trial allowances can only replace periodic meters, not lifetime capacity.")
            .WithErrorCode("subscription_lifetime_meter_trial_grant_invalid");
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

    private static bool EveryEntitlementMeterExists(PlanDefinitionRequest request)
    {
        var meterKeys = request.Meters
            .Select(meter => meter.MeterKey)
            .ToHashSet(StringComparer.Ordinal);

        return request.Entitlements.TrueForAll(entitlement =>
            string.IsNullOrWhiteSpace(entitlement.MeterKey) ||
            meterKeys.Contains(entitlement.MeterKey));
    }

    private static bool EveryTrialGrantMeterExists(PlanDefinitionRequest request)
    {
        var meterKeys = request.Meters
            .Select(meter => meter.MeterKey)
            .ToHashSet(StringComparer.Ordinal);

        return request.TrialGrants.TrueForAll(grant => meterKeys.Contains(grant.MeterKey));
    }
}
