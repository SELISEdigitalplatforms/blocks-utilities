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
    private readonly ISubscriptionContextResolver _context;
    private readonly IValidator<CreateDiscountRequest> _validator;

    public DiscountCatalogueService(
        ISubscriptionDiscountRepository discounts,
        ISubscriptionContextResolver context,
        IValidator<CreateDiscountRequest> validator)
    {
        _discounts = discounts;
        _context = context;
        _validator = validator;
    }

    public async Task<SubscriptionOperationResult<DiscountResponse>> CreateAsync(
        CreateDiscountRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<DiscountResponse>(correlationId);
        var invalid = await SubscriptionValidation.CheckAsync<CreateDiscountRequest, DiscountResponse>(
            _validator, request, "subscription_discount_invalid", "The discount is invalid.",
            correlationId, cancellationToken);
        if (invalid is not null) return invalid;

        var context = resolution.Context!;
        var discount = new Discount
        {
            TenantId = context.TenantId,
            OrganizationId = string.IsNullOrWhiteSpace(request.OrganizationId) ? null : request.OrganizationId,
            Code = request.Code.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName.Trim(),
            CurrencyCode = request.CurrencyCode?.Trim().ToUpperInvariant(),
            ApplicablePlanCodes = request.ApplicablePlanCodes.Distinct(StringComparer.Ordinal).ToList(),
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
        Status = item.Status.ToString()
    };
}
