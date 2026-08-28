using FluentValidation;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public sealed class DiscountCatalogueService : IDiscountCatalogueService
{
    private readonly ISubscriptionDiscountRepository _discounts;
    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly ISubscriptionContextResolver _context;
    private readonly IValidator<CreateDiscountRequest> _createValidator;
    private readonly IValidator<UpdateDiscountRequest> _updateValidator;
    private readonly TimeProvider _time;

    public DiscountCatalogueService(
        ISubscriptionDiscountRepository discounts,
        ISubscriptionCatalogueRepository catalogue,
        ISubscriptionContextResolver context,
        IValidator<CreateDiscountRequest> createValidator,
        IValidator<UpdateDiscountRequest> updateValidator,
        TimeProvider? time = null)
    {
        _discounts = discounts;
        _catalogue = catalogue;
        _context = context;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<DiscountResponse>> CreateAsync(
        CreateDiscountRequest request, string correlationId, CancellationToken cancellationToken)
    {
        // Resolved against the organization the request names, the way listing and archiving already
        // are. Passing null here asked "who is calling" and then stored the answer to a different
        // question: the console authoring a discount for a customer had its restrictions validated
        // against the console's own catalogue, where that customer's plans do not appear.
        var resolution = await _context.ResolveAsync(
            correlationId, request.OrganizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<DiscountResponse>(correlationId);
        var invalid = await SubscriptionValidation.CheckAsync<CreateDiscountRequest, DiscountResponse>(
            _createValidator, request, "subscription_discount_invalid", "The discount is invalid.",
            correlationId, cancellationToken);
        if (invalid is not null) return invalid;

        var context = resolution.Context!;

        var applicability = await CheckApplicabilityAsync(
            request, context.TenantId, context.OrganizationId, correlationId, cancellationToken);
        if (applicability is not null) return applicability;

        var campaign = BuildCampaignTerms(request, out var campaignInvalid);
        if (campaignInvalid is not null)
        {
            return Invalid(campaignInvalid, correlationId);
        }

        var discount = new Discount
        {
            TenantId = context.TenantId,
            // Null is tenant-wide, which is a scope rather than an organization and has to survive
            // resolution. Otherwise the *resolved* organization, never the requested one: the
            // resolver honours a named organization only for the console, so anyone else naming
            // somebody else's ends up scoped to their own instead of writing into it.
            OrganizationId = string.IsNullOrWhiteSpace(request.OrganizationId)
                ? null
                : context.OrganizationId,
            Code = request.Code.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName.Trim(),
            CurrencyCode = request.CurrencyCode?.Trim().ToUpperInvariant(),
            ApplicablePlanCodes = request.ApplicablePlanCodes.Distinct(StringComparer.Ordinal).ToList(),
            ApplicablePriceIds = request.ApplicablePriceIds.Distinct(StringComparer.Ordinal).ToList(),
            Terms = new DiscountTerms
            {
                Code = request.Code.Trim().ToLowerInvariant(),
                Kind = request.Kind,
                PercentBasisPoints = request.PercentBasisPoints,
                AmountMinor = request.AmountMinor,
                DurationPeriods = request.DurationPeriods,
                ExpiresAtUtc = request.ExpiresAtUtc
            },
            Campaign = campaign!
        };

        if (!await _discounts.TryCreateAsync(discount, cancellationToken))
            return SubscriptionOperationResult<DiscountResponse>.Failure(
                PaymentFailureKind.Conflict, "subscription_discount_exists",
                "A discount with this code already exists at this scope.", correlationId);
        return SubscriptionOperationResult<DiscountResponse>.Success(Map(discount), correlationId);
    }

    public async Task<SubscriptionOperationResult<DiscountResponse>> GetAsync(
        string discountId, string? organizationId, string correlationId, CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<DiscountResponse>(correlationId);
        var context = resolution.Context!;

        var item = await _discounts.FindByIdAsync(context.TenantId, discountId, cancellationToken);

        // Visible to this caller means owned by the caller's own scope or unscoped/tenant-wide --
        // the same rule ListAsync already applies, restated here because a lookup by id has no
        // list to filter.
        if (item is null || !VisibleTo(item, context.OrganizationId))
            return SubscriptionOperationResult<DiscountResponse>.Failure(
                PaymentFailureKind.NotFound, "subscription_discount_not_found",
                "The discount does not exist.", correlationId);

        return SubscriptionOperationResult<DiscountResponse>.Success(Map(item), correlationId);
    }

    public async Task<SubscriptionOperationResult<DiscountResponse>> UpdateAsync(
        string discountId,
        UpdateDiscountRequest request,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<DiscountResponse>(correlationId);
        var context = resolution.Context!;

        var invalid = await SubscriptionValidation.CheckAsync<UpdateDiscountRequest, DiscountResponse>(
            _updateValidator, request, "subscription_discount_invalid", "The discount is invalid.",
            correlationId, cancellationToken);
        if (invalid is not null) return invalid;

        var existing = await _discounts.FindByIdAsync(context.TenantId, discountId, cancellationToken);
        if (existing is null || !VisibleTo(existing, context.OrganizationId))
            return SubscriptionOperationResult<DiscountResponse>.Failure(
                PaymentFailureKind.NotFound, "subscription_discount_not_found",
                "The discount does not exist.", correlationId);

        // Code and scope are immutable, so the applicability check below is run with the request's
        // plan/price restrictions against the discount's own (unchangeable) organization -- never
        // against whatever the request happened to resolve to, which for a tenant-wide discount
        // would be the wrong catalogue to check against. SubscriptionContext.OrganizationId is
        // non-nullable and cannot represent "tenant-wide", which is why the scope is passed
        // straight through rather than rebuilding a context around it.
        var applicability = await CheckApplicabilityAsync(
            request, context.TenantId, existing.OrganizationId, correlationId, cancellationToken);
        if (applicability is not null) return applicability;

        var campaign = BuildCampaignTerms(request, out var campaignInvalid);
        if (campaignInvalid is not null)
        {
            return Invalid(campaignInvalid, correlationId);
        }

        existing.DisplayName = request.DisplayName.Trim();
        existing.CurrencyCode = request.CurrencyCode?.Trim().ToUpperInvariant();
        existing.ApplicablePlanCodes =
            request.ApplicablePlanCodes.Distinct(StringComparer.Ordinal).ToList();
        existing.ApplicablePriceIds =
            request.ApplicablePriceIds.Distinct(StringComparer.Ordinal).ToList();
        existing.Terms = new DiscountTerms
        {
            Code = existing.Code,
            Kind = request.Kind,
            PercentBasisPoints = request.PercentBasisPoints,
            AmountMinor = request.AmountMinor,
            DurationPeriods = request.DurationPeriods,
            ExpiresAtUtc = request.ExpiresAtUtc
        };
        existing.Campaign = campaign!;

        if (!await _discounts.TryUpdateAsync(existing, request.ExpectedVersion, cancellationToken))
        {
            // Distinguishing "gone since you read it" from "somebody else edited it" needs another
            // read: TryUpdateAsync's filter cannot tell the two apart, because a document that no
            // longer matches the version filter looks identical whether the row vanished or just
            // moved on.
            var stillThere = await _discounts.FindByIdAsync(context.TenantId, discountId, cancellationToken);
            return stillThere is null
                ? SubscriptionOperationResult<DiscountResponse>.Failure(
                    PaymentFailureKind.NotFound, "subscription_discount_not_found",
                    "The discount does not exist.", correlationId)
                : SubscriptionOperationResult<DiscountResponse>.Failure(
                    PaymentFailureKind.Conflict, "subscription_discount_version_conflict",
                    "Another update has already been applied. Reload the discount and try again.",
                    correlationId);
        }

        return SubscriptionOperationResult<DiscountResponse>.Success(Map(existing), correlationId);
    }

    /// <summary>
    /// Whether an unscoped (tenant-wide) or organization-scoped discount is visible to a caller
    /// resolved for this organization. Mirrors <see cref="ISubscriptionDiscountRepository.ListAsync"/>'s
    /// own rule, restated here for a single-item lookup that has no list to filter.
    /// </summary>
    private static bool VisibleTo(Discount item, string? organizationId) =>
        item.OrganizationId is null ||
        string.Equals(item.OrganizationId, organizationId, StringComparison.Ordinal);

    /// <summary>
    /// Builds the campaign sub-document from a request, computing and freezing
    /// <see cref="CampaignTerms.RedeemableFromUtc"/> and <see cref="CampaignTerms.RedeemableUntilUtc"/>
    /// at authoring time.
    /// </summary>
    /// <remarks>
    /// Computed once here rather than re-derived at every redemption check, so a time-zone
    /// database update between authoring and redemption cannot silently move a boundary a
    /// subscriber was already shown when the code was offered to them.
    /// <para>
    /// The end date is inclusive as authored and exclusive as stored: <c>RedeemableUntilUtc</c> is
    /// local midnight of the day <em>after</em> <c>ValidThroughDate</c>, so the entire authored end
    /// date is still redeemable. Both ends go through <see cref="BillingLocalTime.ToUtc"/>, which
    /// already carries the DST gap/ambiguity policy this billing domain uses everywhere else --
    /// reusing it here rather than writing a second one is the point.
    /// </para>
    /// </remarks>
    private static CampaignTerms? BuildCampaignTerms(
        ICampaignDiscountRequest request, out string? error)
    {
        error = null;

        if (request.CampaignKind == CampaignKind.Standard)
        {
            return new CampaignTerms();
        }

        if (!BillingLocalTime.TryFindTimeZone(request.TimeZoneId, out var timeZone))
        {
            error = $"'{request.TimeZoneId}' is not a recognised time zone.";
            return null;
        }

        // Validated non-null by CampaignDiscountRequestValidator whenever CampaignKind is not
        // Standard -- asserted rather than re-checked, since this is only ever called after that
        // validator has already run.
        var from = request.ValidFromDate!.Value;
        var through = request.ValidThroughDate!.Value;

        var redeemableFromUtc = BillingLocalTime.ToUtc(from.ToDateTime(TimeOnly.MinValue), timeZone);
        var redeemableUntilUtc = BillingLocalTime.ToUtc(
            through.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);

        return new CampaignTerms
        {
            Kind = request.CampaignKind,
            Precedence = request.CampaignPrecedence,
            ValidFromDate = from,
            ValidThroughDate = through,
            TimeZoneId = request.TimeZoneId,
            RedeemableFromUtc = redeemableFromUtc,
            RedeemableUntilUtc = redeemableUntilUtc,
            OneUsePerOrganization = request.OneUsePerOrganization,
            ApplyToOpeningStub = request is CreateDiscountRequest { ApplyToOpeningStub: true } or
                UpdateDiscountRequest { ApplyToOpeningStub: true },
            RequiresPaymentMethodUpfront = request.RequiresPaymentMethodUpfront,
            EntitlementOverride = request.EntitlementOverrideKey is { Length: > 0 } key
                ? new CampaignEntitlementOverride
                {
                    EntitlementKey = key,
                    Limit = request.EntitlementOverrideLimit!.Value
                }
                : null
        };
    }

    /// <summary>
    /// Checks that the plans and prices a discount is restricted to actually exist, in this caller's
    /// scope, agree with each other, and -- for a campaign -- carry a cadence that campaign kind can
    /// actually price.
    /// </summary>
    /// <remarks>
    /// A restriction naming something that does not exist is a discount that can never be redeemed:
    /// every attempt is refused with <c>subscription_discount_not_applicable</c>, and nothing about
    /// that error tells the author their code has a typo in it. The portal picks from a list and
    /// cannot make the mistake; an API client, a script, or a copied identifier from another
    /// environment can, so it is refused where the author can still fix it.
    /// <para>
    /// A price is also checked against the plan list when both are given, because with both narrowing
    /// each other a price belonging to an unlisted plan matches nothing -- the same unredeemable
    /// discount, arrived at by a different mistake.
    /// </para>
    /// </remarks>
    private async Task<SubscriptionOperationResult<DiscountResponse>?> CheckApplicabilityAsync(
        ICampaignDiscountRequest request,
        string tenantId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (request.ApplicablePlanCodes.Count == 0 && request.ApplicablePriceIds.Count == 0)
        {
            // Unrestricted, which is the ordinary case and has nothing to resolve. A campaign kind
            // that needs at least one price is already refused by the request validator before
            // this is reached, so an unrestricted request here is never a campaign one.
            return null;
        }

        // The plans this caller can actually sell, which is the same list a subscribe request is
        // resolved against -- the discount cannot be more applicable than the catalogue it points at.
        // Already filtered to CatalogueStatus.Active, so a plan named here that has been archived
        // reads as unknown -- the same refusal an actually-nonexistent code would produce.
        var plans = await _catalogue.ListPlansAsync(tenantId, organizationId, cancellationToken);

        var unknownPlan = request.ApplicablePlanCodes.Find(code =>
            !plans.Any(plan => string.Equals(plan.Code, code, StringComparison.Ordinal)));

        if (unknownPlan is not null)
        {
            return Invalid($"No plan with the code '{unknownPlan}' is available here.", correlationId);
        }

        foreach (var priceId in request.ApplicablePriceIds)
        {
            var price = await _catalogue.GetPriceAsync(tenantId, priceId, cancellationToken);

            // Unlike ListPlansAsync, GetPriceAsync does not filter by status -- it exists to look
            // up one specific id regardless of whether it can still be sold, which every other
            // caller of it wants. A new campaign must not be authored against a price nobody can
            // buy any more, so that is checked here rather than assumed from the plan check above.
            if (price is not null && price.Status != CatalogueStatus.Active)
            {
                return Invalid(
                    $"The price '{priceId}' has been retired and cannot be used for a new discount.",
                    correlationId);
            }

            // Visible to this caller means: on a plan this caller can see. A price id from another
            // organization reads as unknown rather than as forbidden, so the refusal cannot be used
            // to confirm that somebody else's price exists.
            var plan = price is null
                ? null
                : plans.FirstOrDefault(candidate => candidate.ItemId == price.PlanId);

            if (plan is null)
            {
                return Invalid($"No price '{priceId}' is available here.", correlationId);
            }

            if (request.ApplicablePlanCodes.Count > 0 &&
                !request.ApplicablePlanCodes.Contains(plan.Code, StringComparer.Ordinal))
            {
                return Invalid(
                    $"The price '{priceId}' belongs to the plan '{plan.Code}', which this discount "
                        + "is not applicable to. Both restrictions have to match, so this discount "
                        + "could never be redeemed.",
                    correlationId);
            }

            var cadence = CheckCadence(request.CampaignKind, priceId, price!);
            if (cadence is not null)
            {
                return Invalid(cadence, correlationId);
            }

            var entitlement = CheckEntitlementOverride(request, plan);
            if (entitlement is not null)
            {
                return Invalid(entitlement, correlationId);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a price's cadence is one this campaign kind can actually price.
    /// </summary>
    /// <remarks>
    /// FirstAnnualPeriod exists to discount an opening stub and a first annual period, neither of
    /// which a non-yearly price has. FreeOpeningCalendarPeriod exists to give away a calendar
    /// month, which only a monthly price billed on calendar boundaries has a month to give.
    /// Authored against the wrong cadence, either campaign would validate cleanly and then never
    /// be redeemable -- refused here instead, at the one point an author can still fix it.
    /// </remarks>
    private static string? CheckCadence(CampaignKind kind, string priceId, Price price) => kind switch
    {
        CampaignKind.FirstAnnualPeriod when price.Interval != BillingInterval.Year
            || price.IntervalCount != 1 =>
            $"The price '{priceId}' does not bill yearly, so a first-annual-period campaign can " +
            "never apply to it.",
        CampaignKind.FreeOpeningCalendarPeriod when !CalendarBillingAlignment.IsCalendarAligned(
                price.BillingAlignment, price.Interval, price.IntervalCount) ||
            price.Interval != BillingInterval.Month =>
            $"The price '{priceId}' is not a monthly, calendar-aligned price, so a " +
            "free-opening-period campaign has no calendar month to give away on it.",
        _ => null
    };

    /// <summary>
    /// Whether a temporary entitlement override actually names something this plan grants, and
    /// caps it no higher than the plan already does.
    /// </summary>
    /// <remarks>
    /// A key that does not exist on the plan is not an override of anything. A limit higher than
    /// the plan's own is not temporary at all -- it would let the campaign grant more than the
    /// plan the subscriber is actually on.
    /// </remarks>
    private static string? CheckEntitlementOverride(ICampaignDiscountRequest request, Plan plan)
    {
        if (request.EntitlementOverrideKey is not { Length: > 0 } key)
        {
            return null;
        }

        var entitlement = plan.Entitlements.FirstOrDefault(
            item => string.Equals(item.Key, key, StringComparison.Ordinal));

        if (entitlement is null)
        {
            return $"The plan '{plan.Code}' has no entitlement '{key}' to temporarily cap.";
        }

        if (entitlement.LimitKind != EntitlementLimitKind.Count)
        {
            return $"The entitlement '{key}' on plan '{plan.Code}' is not a count, so it has no " +
                "limit for a campaign to temporarily override.";
        }

        if (request.EntitlementOverrideLimit is { } limit &&
            entitlement.Limit is { } planLimit && limit > planLimit)
        {
            return $"The temporary limit for '{key}' cannot exceed the plan's own limit of " +
                $"{planLimit}.";
        }

        return null;
    }

    private static SubscriptionOperationResult<DiscountResponse> Invalid(
        string message,
        string correlationId) =>
        SubscriptionOperationResult<DiscountResponse>.Failure(
            PaymentFailureKind.Validation,
            "subscription_discount_applicability_invalid",
            message,
            correlationId);

    public async Task<SubscriptionOperationResult<IReadOnlyList<DiscountResponse>>> ListAsync(
        string? organizationId, string correlationId, CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<IReadOnlyList<DiscountResponse>>(correlationId);
        var context = resolution.Context!;
        var items = await _discounts.ListAsync(context.TenantId, context.OrganizationId, cancellationToken);
        return SubscriptionOperationResult<IReadOnlyList<DiscountResponse>>.Success(items.Select(Map).ToList(), correlationId);
    }

    public async Task<SubscriptionOperationResult<DiscountResponse>> ArchiveAsync(
        string discountId, string? organizationId, string correlationId, CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<DiscountResponse>(correlationId);
        var context = resolution.Context!;
        var item = (await _discounts.ListAsync(context.TenantId, context.OrganizationId, cancellationToken))
            .FirstOrDefault(discount => discount.ItemId == discountId);
        if (item is null || !await _discounts.TryArchiveAsync(context.TenantId, discountId, cancellationToken))
            return SubscriptionOperationResult<DiscountResponse>.Failure(
                PaymentFailureKind.NotFound, "subscription_discount_not_found",
                "The discount does not exist or is already retired.", correlationId);
        item.Status = CatalogueStatus.Archived;
        return SubscriptionOperationResult<DiscountResponse>.Success(Map(item), correlationId);
    }

    private DiscountResponse Map(Discount item) => new()
    {
        DiscountId = item.ItemId, OrganizationId = item.OrganizationId, Code = item.Code,
        DisplayName = item.DisplayName, Kind = item.Terms.Kind.ToString(),
        PercentBasisPoints = item.Terms.PercentBasisPoints, AmountMinor = item.Terms.AmountMinor,
        CurrencyCode = item.CurrencyCode, DurationPeriods = item.Terms.DurationPeriods,
        ExpiresAtUtc = item.Terms.ExpiresAtUtc, ApplicablePlanCodes = [.. item.ApplicablePlanCodes],
        ApplicablePriceIds = [.. item.ApplicablePriceIds],
        Status = item.Status.ToString(),
        Version = item.Version,
        CampaignKind = item.Campaign.Kind.ToString(),
        CampaignPrecedence = item.Campaign.Precedence.ToString(),
        ValidFromDate = item.Campaign.ValidFromDate,
        ValidThroughDate = item.Campaign.ValidThroughDate,
        TimeZoneId = item.Campaign.TimeZoneId,
        RedeemableFromUtc = item.Campaign.RedeemableFromUtc,
        RedeemableUntilUtc = item.Campaign.RedeemableUntilUtc,
        OneUsePerOrganization = item.Campaign.OneUsePerOrganization,
        ApplyToOpeningStub = item.Campaign.ApplyToOpeningStub,
        RequiresPaymentMethodUpfront = item.Campaign.RequiresPaymentMethodUpfront,
        EntitlementOverrideKey = item.Campaign.EntitlementOverride?.EntitlementKey,
        EntitlementOverrideLimit = item.Campaign.EntitlementOverride?.Limit,
        EffectiveState = EffectiveState(item)
    };

    /// <summary>
    /// Upcoming, Active, Expired or Archived, for the catalogue list -- computed rather than
    /// stored, so it is never stale between a boundary passing and the next time this document is
    /// written.
    /// </summary>
    /// <remarks>
    /// A <see cref="CampaignKind.Standard"/> discount has no window, so it is Active whenever its
    /// <see cref="CatalogueStatus"/> is, exactly as before this field existed -- Archived is the
    /// only state a legacy discount can be told apart by.
    /// </remarks>
    private string EffectiveState(Discount item)
    {
        if (item.Status == CatalogueStatus.Archived)
        {
            return "Archived";
        }

        if (item.Campaign.Kind == CampaignKind.Standard)
        {
            return "Active";
        }

        var now = _time.GetUtcNow().UtcDateTime;

        if (item.Campaign.RedeemableFromUtc is { } from && now < from)
        {
            return "Upcoming";
        }

        if (item.Campaign.RedeemableUntilUtc is { } until && now >= until)
        {
            return "Expired";
        }

        return "Active";
    }
}
