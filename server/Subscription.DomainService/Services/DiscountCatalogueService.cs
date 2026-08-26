using FluentValidation;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public sealed class DiscountCatalogueService : IDiscountCatalogueService
{
    private readonly ISubscriptionDiscountRepository _discounts;
    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly ISubscriptionContextResolver _context;
    private readonly IValidator<CreateDiscountRequest> _validator;

    public DiscountCatalogueService(
        ISubscriptionDiscountRepository discounts,
        ISubscriptionCatalogueRepository catalogue,
        ISubscriptionContextResolver context,
        IValidator<CreateDiscountRequest> validator)
    {
        _discounts = discounts;
        _catalogue = catalogue;
        _context = context;
        _validator = validator;
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
            _validator, request, "subscription_discount_invalid", "The discount is invalid.",
            correlationId, cancellationToken);
        if (invalid is not null) return invalid;

        var context = resolution.Context!;

        var applicability = await CheckApplicabilityAsync(
            request, context, correlationId, cancellationToken);
        if (applicability is not null) return applicability;

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
            }
        };

        if (!await _discounts.TryCreateAsync(discount, cancellationToken))
            return SubscriptionOperationResult<DiscountResponse>.Failure(
                PaymentFailureKind.Conflict, "subscription_discount_exists",
                "A discount with this code already exists at this scope.", correlationId);
        return SubscriptionOperationResult<DiscountResponse>.Success(Map(discount), correlationId);
    }

    /// <summary>
    /// Checks that the plans and prices a discount is restricted to actually exist, in this caller's
    /// scope, and agree with each other. Null when they do.
    /// </summary>
    /// <remarks>
    /// A restriction naming something that does not exist is a discount that can never be redeemed:
    /// every attempt is refused with <c>subscription_discount_not_applicable</c>, and nothing about
    /// that error tells the author their code has a typo in it. The portal picks from a list and
    /// cannot make the mistake; an API client, a script, or a copied identifier from another
    /// environment can, so it is refused where the author can still fix it.
    /// <para>
    /// A price is also checked against the plan list when both are given, because with both narrowing
    /// each other a price belonging to an unlisted plan matches nothing — the same unredeemable
    /// discount, arrived at by a different mistake.
    /// </para>
    /// </remarks>
    private async Task<SubscriptionOperationResult<DiscountResponse>?> CheckApplicabilityAsync(
        CreateDiscountRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (request.ApplicablePlanCodes.Count == 0 && request.ApplicablePriceIds.Count == 0)
        {
            // Unrestricted, which is the ordinary case and has nothing to resolve.
            return null;
        }

        // The plans this caller can actually sell, which is the same list a subscribe request is
        // resolved against — the discount cannot be more applicable than the catalogue it points at.
        var plans = await _catalogue.ListPlansAsync(
            context.TenantId, context.OrganizationId, cancellationToken);

        var unknownPlan = request.ApplicablePlanCodes.Find(code =>
            !plans.Any(plan => string.Equals(plan.Code, code, StringComparison.Ordinal)));

        if (unknownPlan is not null)
        {
            return Invalid($"No plan with the code '{unknownPlan}' is available here.", correlationId);
        }

        foreach (var priceId in request.ApplicablePriceIds)
        {
            var price = await _catalogue.GetPriceAsync(context.TenantId, priceId, cancellationToken);

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

    private static DiscountResponse Map(Discount item) => new()
    {
        DiscountId = item.ItemId, OrganizationId = item.OrganizationId, Code = item.Code,
        DisplayName = item.DisplayName, Kind = item.Terms.Kind.ToString(),
        PercentBasisPoints = item.Terms.PercentBasisPoints, AmountMinor = item.Terms.AmountMinor,
        CurrencyCode = item.CurrencyCode, DurationPeriods = item.Terms.DurationPeriods,
        ExpiresAtUtc = item.Terms.ExpiresAtUtc, ApplicablePlanCodes = [.. item.ApplicablePlanCodes],
        ApplicablePriceIds = [.. item.ApplicablePriceIds],
        Status = item.Status.ToString()
    };
}
