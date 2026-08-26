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
            .When(request => request.TrialDays.HasValue && !request.TrialDurationKind.HasValue);

        RuleFor(request => request)
            .Must(request => !(request.TrialDays.HasValue && request.TrialDurationKind.HasValue))
            .WithName(nameof(PlanDefinitionRequest.TrialDurationKind))
            .WithMessage(
                "Use either the legacy trialDays field or trialDurationKind/trialDurationCount, " +
                "not both.")
            .WithErrorCode("subscription_trial_duration_fields_conflict");

        RuleFor(request => request.TrialDurationCount)
            .NotNull()
            .WithMessage("A day-based trial requires a count.")
            .DependentRules(() => RuleFor(request => request.TrialDurationCount)
                .InclusiveBetween(1, 365)
                .WithMessage("A day-based trial's count must be between 1 and 365."))
            .When(request => request.TrialDurationKind == TrialDurationKind.Days);

        RuleFor(request => request.TrialDurationCount)
            .NotNull()
            .WithMessage("An anniversary-month trial requires a count.")
            .DependentRules(() => RuleFor(request => request.TrialDurationCount)
                .InclusiveBetween(1, 12)
                .WithMessage("An anniversary-month trial's count must be between 1 and 12."))
            .When(request => request.TrialDurationKind == TrialDurationKind.AnniversaryMonths);

        RuleFor(request => request.TrialDurationCount)
            .Null()
            .WithMessage("An end-of-calendar-month trial must not specify a count.")
            .When(request => request.TrialDurationKind == TrialDurationKind.EndOfCalendarMonth);

        RuleFor(request => request.TrialDurationCount)
            .Null()
            .WithMessage("trialDurationCount requires trialDurationKind.")
            .When(request => !request.TrialDurationKind.HasValue);

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
                item.RuleForEach(quantity => quantity.QuantityDiscountTiers)
                    .ChildRules(tier =>
                    {
                        tier.RuleFor(band => band.MinimumQuantity).GreaterThan(0);
                        tier.RuleFor(band => band.DiscountBasisPoints).InclusiveBetween(0, 10_000);
                        tier.RuleFor(band => band)
                            .Must(band =>
                                band.MaximumQuantity is null ||
                                band.MaximumQuantity >= band.MinimumQuantity)
                            .WithName(nameof(QuantityDiscountTierRequest.MaximumQuantity))
                            .WithMessage("A band's maximum cannot be below its minimum.");
                    });
                item.RuleFor(quantity => quantity)
                    .Must(BeContiguousBands)
                    .WithName(nameof(PlanQuantityItemRequest.QuantityDiscountTiers))
                    .WithMessage(
                        "Bands must ascend from the item's minimum quantity without gaps or " +
                        "overlaps, and only the last may be open-ended.")
                    .WithErrorCode("subscription_quantity_discount_tiers_invalid");
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
                meter.RuleFor(definition => definition)
                    .Must(definition =>
                        definition.ResetPolicy != MeterResetPolicy.CarryForward ||
                        definition.CarryForwardCap is > 0)
                    .WithMessage(
                        "A carry-forward meter needs a positive cap on what one period may " +
                        "carry in. Without one a dormant subscription banks allowance forever.")
                    .WithErrorCode("subscription_carry_forward_cap_required");
                meter.RuleFor(definition => definition)
                    .Must(definition =>
                        definition.ResetPolicy == MeterResetPolicy.CarryForward ||
                        definition.CarryForwardCap is null)
                    .WithMessage(
                        "Only a carry-forward meter has a carry-forward cap.")
                    .WithErrorCode("subscription_carry_forward_cap_unexpected");
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
                    // Any resetting meter, not Periodic alone: a carry-forward meter replenishes
                    // per window too, so a trial may replace its allowance the same way.
                    meter.ResetPolicy != MeterResetPolicy.Never)))
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

    /// <summary>
    /// Whether an item's volume bands cover every quantity it can hold, exactly once.
    /// </summary>
    /// <remarks>
    /// A gap or an overlap is not a cosmetic problem. A quantity landing in a gap resolves to no
    /// band and is charged full price, which reads to the customer as the discount silently
    /// vanishing; a quantity landing in two bands is charged whichever the resolver happens to
    /// match first, so the bill depends on document order. Both are refused at authoring time
    /// rather than discovered on an invoice.
    /// <para>
    /// Coverage starts at the item's own minimum, not at one: an item whose minimum is 5 has
    /// nothing to say about a quantity of 3, which cannot be bought.
    /// </para>
    /// </remarks>
    private static bool BeContiguousBands(PlanQuantityItemRequest item)
    {
        var bands = item.QuantityDiscountTiers;

        if (bands.Count == 0)
        {
            return true;
        }

        // Only the final band may be open-ended; an unbounded one in the middle would swallow
        // every band after it.
        if (bands.Take(bands.Count - 1).Any(band => band.MaximumQuantity is null))
        {
            return false;
        }

        if (bands[0].MinimumQuantity != Math.Max(1, item.MinQuantity))
        {
            return false;
        }

        for (var index = 1; index < bands.Count; index++)
        {
            // Each band must begin exactly where the last ended: anything else is a gap or an
            // overlap, and the difference between them is only the sign.
            if (bands[index - 1].MaximumQuantity is not { } previousMaximum ||
                bands[index].MinimumQuantity != previousMaximum + 1)
            {
                return false;
            }
        }

        var last = bands[^1];

        return last.MaximumQuantity is null ||
               item.MaxQuantity is null ||
               last.MaximumQuantity >= item.MaxQuantity;
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
