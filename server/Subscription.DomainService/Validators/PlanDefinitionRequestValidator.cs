using System.Text.Json;
using FluentValidation;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Utilities;

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
                // Each pace on its own terms. A window is always present on the list form, so the
                // half-written cap the single fields had to guard against cannot be sent at all.
                meter.RuleForEach(definition => definition.SubLimits)
                    .ChildRules(limit =>
                    {
                        limit.RuleFor(pace => pace.Window)
                            .IsInEnum()
                            .WithMessage("A sub-limit window is an hour, a day or a week.")
                            .WithErrorCode("subscription_meter_sub_limit_invalid");

                        limit.RuleFor(pace => pace.Quantity)
                            .GreaterThan(0)
                            .WithMessage(
                                "A sub-limit of zero refuses everything. Remove it to cap by " +
                                "period alone.")
                            .WithErrorCode("subscription_meter_sub_limit_invalid");

                        limit.RuleFor(pace => pace.WindowCount)
                            .GreaterThan(0)
                            .WithMessage("A sub-limit spans at least one window.")
                            .WithErrorCode("subscription_meter_sub_limit_invalid");

                        // A fixed window only tiles a day evenly when its length divides one — five
                        // hours does not, and would leave one short block a day with nowhere
                        // consistent to start it. Rolling has no start on the clock to tile from.
                        limit.RuleFor(pace => pace.WindowCount)
                            .Must(TilesADay)
                            .When(pace => !pace.Rolling && pace.Window == UsageWindow.Hour)
                            .WithMessage(
                                "A fixed hourly window has to divide a day evenly — 1, 2, 3, 4, " +
                                "6, 8, 12 or 24 — or mark it rolling instead.")
                            .WithErrorCode("subscription_meter_sub_limit_invalid");
                    });

                meter.RuleFor(definition => definition.SubLimits)
                    .Must(limits => limits.Count <= MaximumSubLimits)
                    .WithMessage($"A meter takes at most {MaximumSubLimits} sub-limits.")
                    .WithErrorCode("subscription_meter_sub_limit_invalid");

                // Two limits of the same length always have one that makes the other pointless —
                // the smaller always binds first. Compared in hours, so a day and twenty-four hours
                // are the same limit however they were written.
                meter.RuleFor(definition => definition.SubLimits)
                    .Must(limits => limits
                        .Select(SpanHours)
                        .Distinct()
                        .Count() == limits.Count)
                    .WithMessage("Two sub-limits on one meter cannot cover the same length of time.")
                    .WithErrorCode("subscription_meter_sub_limit_invalid");

                // A longer limit allowing no more than a shorter one leaves the shorter one unable
                // ever to bite: 1,000 an hour beside 500 a day is 500 a day, and the hourly figure
                // is a promise the plan never keeps.
                meter.RuleFor(definition => definition.SubLimits)
                    .Must(LongerLimitsAllowMore)
                    .WithMessage(
                        "A longer sub-limit has to allow more than a shorter one, or the shorter " +
                        "one can never apply.")
                    .WithErrorCode("subscription_meter_sub_limit_invalid");

                meter.RuleFor(definition => definition.QuantityScale)
                    .InclusiveBetween(0, MeterQuantity.MaxScale)
                    .WithMessage(
                        "A meter allows at most six decimal places. Zero, the default, means " +
                        "whole numbers only.")
                    .WithErrorCode("subscription_meter_quantity_scale_invalid");
                meter.RuleFor(definition => definition)
                    .Must(HaveQuantitiesWithinScale)
                    .WithName(nameof(PlanMeterRequest.IncludedQuantity))
                    .WithMessage(
                        "A quantity has more decimal places than this meter allows, or is larger " +
                        "than a quantity may be. Raise the meter's decimal places to allow it.")
                    .WithErrorCode("subscription_meter_quantity_scale_exceeded");
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
            .Must(EveryEntitlementLimitIsWithinItsMeterScale)
            .WithName(nameof(PlanDefinitionRequest.Entitlements))
            .WithMessage(
                "An entitlement's limit has more decimal places than the meter it draws down " +
                "allows. The limit is compared against that meter's balance, so it has to be a " +
                "quantity that meter can hold.")
            .WithErrorCode("subscription_entitlement_limit_quantity_scale_exceeded");

        RuleFor(request => request)
            .Must(EveryTrialGrantIsWithinItsMeterScale)
            .WithName(nameof(PlanDefinitionRequest.TrialGrants))
            .WithMessage(
                "A trial grant has more decimal places than its meter allows. A grant replaces " +
                "that meter's allowance, so it has to be a quantity that meter can hold.")
            .WithErrorCode("subscription_trial_grant_quantity_scale_exceeded");
    }

    /// <summary>
    /// Every quantity a meter carries has to be one that meter can actually hold.
    /// </summary>
    /// <remarks>
    /// The allowance, the carry-forward cap and each rate band's bound, all against the meter's own
    /// declared scale. A bound the meter cannot represent would put a fractional overage in a band
    /// whose edge sits between two representable quantities.
    /// <para>
    /// Magnitude is checked here too. Decimal128 holds more than a <c>decimal</c> does, so a bound
    /// large enough to overflow on the way back in is refused at authoring time — the one moment
    /// there is a person to tell.
    /// </para>
    /// </remarks>
    private static bool HaveQuantitiesWithinScale(PlanMeterRequest meter)
    {
        if (!MeterQuantity.IsValidScale(meter.QuantityScale))
        {
            // Its own rule reports this; saying it twice would put two messages on one mistake.
            return true;
        }

        bool Fits(decimal value) =>
            MeterQuantity.IsWithinMagnitude(value) &&
            MeterQuantity.IsWithinScale(value, meter.QuantityScale);

        return Fits(meter.IncludedQuantity) &&
               (meter.CarryForwardCap is not { } cap || Fits(cap)) &&
               meter.RateTables.TrueForAll(table =>
                   table.Tiers.TrueForAll(tier =>
                       tier.UpToQuantity is not { } bound || Fits(bound)));
    }

    /// <summary>
    /// A counted entitlement's limit has to be a quantity its meter can hold.
    /// </summary>
    /// <remarks>
    /// The limit is what <see cref="Services.EntitlementService"/> compares the meter's balance
    /// against, and what it subtracts that balance from to report what remains. A limit the meter
    /// cannot represent would advertise an allowance the usage gate could never agree with — the
    /// disagreement <c>LimitFor</c>'s own remarks warn about.
    /// <para>
    /// An entitlement naming no meter is a plain cap on something this module does not count, so it
    /// is held only to the platform maximum rather than to any meter's scale.
    /// </para>
    /// </remarks>
    private static bool EveryEntitlementLimitIsWithinItsMeterScale(PlanDefinitionRequest request) =>
        request.Entitlements.TrueForAll(entitlement =>
        {
            if (entitlement.Limit is not { } limit)
            {
                return true;
            }

            var meter = request.Meters.Find(candidate =>
                string.Equals(candidate.MeterKey, entitlement.MeterKey, StringComparison.Ordinal));

            // A meter that does not exist, or whose scale is not a scale, is already refused by its
            // own rule; saying so twice would put two messages on one mistake.
            if (entitlement.MeterKey is { Length: > 0 } && meter is null)
            {
                return true;
            }

            var scale = meter is null ? MeterQuantity.MaxScale : meter.QuantityScale;

            if (!MeterQuantity.IsValidScale(scale))
            {
                return true;
            }

            return MeterQuantity.IsWithinMagnitude(limit) &&
                   MeterQuantity.IsWithinScale(limit, scale);
        });

    private static bool EveryTrialGrantIsWithinItsMeterScale(PlanDefinitionRequest request) =>
        request.TrialGrants.TrueForAll(grant =>
        {
            var meter = request.Meters.Find(candidate =>
                string.Equals(candidate.MeterKey, grant.MeterKey, StringComparison.Ordinal));

            // A grant naming no meter, or a meter with an invalid scale, is already refused by its
            // own rule.
            if (meter is null || !MeterQuantity.IsValidScale(meter.QuantityScale))
            {
                return true;
            }

            return MeterQuantity.IsWithinMagnitude(grant.IncludedQuantity) &&
                   MeterQuantity.IsWithinScale(grant.IncludedQuantity, meter.QuantityScale);
        });

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
    /// <summary>
    /// Whether this many hours fit a day with none left over.
    /// </summary>
    /// <remarks>
    /// The set is 1, 2, 3, 4, 6, 8, 12 and 24 — every divisor of 24 — because a fixed window that
    /// does not divide a day leaves one short block a day, and no two days start their windows at
    /// the same clock time. Five is the case this exists to catch: it looks like a reasonable
    /// number and is not one that tiles.
    /// </remarks>
    private static bool TilesADay(int hours) => hours is 1 or 2 or 3 or 4 or 6 or 8 or 12 or 24;

    private const int MaximumSubLimits = 3;

    private static int SpanHours(PlanMeterSubLimitRequest limit) =>
        Math.Max(1, limit.WindowCount) * limit.Window switch
        {
            UsageWindow.Day => 24,
            UsageWindow.Week => 24 * 7,
            _ => 1
        };

    private static bool LongerLimitsAllowMore(List<PlanMeterSubLimitRequest> limits)
    {
        var bySpan = limits.OrderBy(SpanHours).ToList();

        return bySpan.Zip(bySpan.Skip(1))
            .All(pair => SpanHours(pair.First) == SpanHours(pair.Second) ||
                pair.Second.Quantity > pair.First.Quantity);
    }

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

    /// <summary>
    /// Also used by <see cref="Subscription.DomainService.Services.PlanCatalogueService"/> to
    /// validate <see cref="Subscription.DomainService.Requests.UpdatePlanMeterRatesRequest"/>,
    /// so a meter's rate tables are held to the same rule whether they arrive with the rest of
    /// the plan or on their own.
    /// </summary>
    internal static bool HaveWellOrderedTiers(List<MeterRateTableRequest> rateTables) =>
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
