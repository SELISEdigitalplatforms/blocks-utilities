using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Authoring and reading what a tenant sells.
/// </summary>
public sealed class PlanCatalogueService : IPlanCatalogueService
{
    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IValidator<CreatePlanRequest> _planValidator;
    private readonly IValidator<CreatePriceRequest> _priceValidator;
    private readonly IPlanResponseMapper _mapper;
    private readonly ILogger<PlanCatalogueService> _logger;

    public PlanCatalogueService(
        ISubscriptionCatalogueRepository catalogue,
        ISubscriptionContextResolver contextResolver,
        IValidator<CreatePlanRequest> planValidator,
        IValidator<CreatePriceRequest> priceValidator,
        IPlanResponseMapper mapper,
        ILogger<PlanCatalogueService> logger)
    {
        _catalogue = catalogue;
        _contextResolver = contextResolver;
        _planValidator = planValidator;
        _priceValidator = priceValidator;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<SubscriptionOperationResult<PlanResponse>> CreatePlanAsync(
        CreatePlanRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            null,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<PlanResponse>(correlationId);
        }

        var invalid = await SubscriptionValidation.CheckAsync<CreatePlanRequest, PlanResponse>(
            _planValidator,
            request,
            "subscription_plan_invalid",
            "The plan is invalid.",
            correlationId,
            cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var context = resolution.Context!;
        var plan = BuildPlan(request, context.TenantId);

        if (!await _catalogue.TryCreatePlanAsync(plan, cancellationToken))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_exists",
                "A plan with this code already exists at this scope.",
                correlationId);
        }

        _logger.LogInformation(
            "Subscription plan created TenantHash={TenantHash} PlanHash={PlanHash} Code={Code} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(plan.ItemId),
            PaymentLogValue.Label(plan.Code),
            correlationId);

        return SubscriptionOperationResult<PlanResponse>.Success(
            _mapper.ToResponse(plan, []),
            correlationId);
    }

    public async Task<SubscriptionOperationResult<PlanResponse>> CreatePriceAsync(
        CreatePriceRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            null,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<PlanResponse>(correlationId);
        }

        var invalid = await SubscriptionValidation.CheckAsync<CreatePriceRequest, PlanResponse>(
            _priceValidator,
            request,
            "subscription_price_invalid",
            "The price is invalid.",
            correlationId,
            cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var context = resolution.Context!;
        var plan = await _catalogue.GetPlanAsync(
            context.TenantId,
            request.PlanId,
            cancellationToken);

        if (plan is null)
        {
            return NotFound(correlationId);
        }

        if (!QuantityItemExists(plan, request.QuantityItemKey))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_item_unknown",
                "The price charges for a quantity item the plan does not define.",
                correlationId);
        }

        var price = new Price
        {
            TenantId = context.TenantId,
            PlanId = plan.ItemId,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            UnitAmountMinor = request.UnitAmountMinor,
            Interval = request.Interval,
            IntervalCount = request.IntervalCount,
            QuantityItemKey = request.QuantityItemKey,
            TaxRateBasisPoints = request.TaxRateBasisPoints,
            Status = CatalogueStatus.Active
        };

        if (!await _catalogue.TryCreatePriceAsync(price, cancellationToken))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_price_exists",
                "A price with these terms already exists for this plan.",
                correlationId);
        }

        _logger.LogInformation(
            "Subscription price created TenantHash={TenantHash} PlanHash={PlanHash} Currency={Currency} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(plan.ItemId),
            PaymentLogValue.Label(price.CurrencyCode),
            correlationId);

        return await GetPlanAsync(
            plan.ItemId,
            context.OrganizationId,
            correlationId,
            cancellationToken);
    }

    public async Task<SubscriptionOperationResult<IReadOnlyList<PlanResponse>>> ListPlansAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            organizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<IReadOnlyList<PlanResponse>>(correlationId);
        }

        var context = resolution.Context!;
        var plans = await _catalogue.ListPlansAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        var responses = new List<PlanResponse>(plans.Count);

        foreach (var plan in plans)
        {
            var prices = await _catalogue.ListPricesAsync(
                context.TenantId,
                plan.ItemId,
                cancellationToken);

            responses.Add(_mapper.ToResponse(plan, prices));
        }

        return SubscriptionOperationResult<IReadOnlyList<PlanResponse>>.Success(
            responses,
            correlationId);
    }

    public async Task<SubscriptionOperationResult<PlanResponse>> GetPlanAsync(
        string planId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            organizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<PlanResponse>(correlationId);
        }

        var context = resolution.Context!;
        var plan = await _catalogue.GetPlanAsync(
            context.TenantId,
            planId,
            cancellationToken);

        if (plan is null || !IsVisibleTo(plan, context.OrganizationId))
        {
            return NotFound(correlationId);
        }

        var prices = await _catalogue.ListPricesAsync(
            context.TenantId,
            plan.ItemId,
            cancellationToken);

        return SubscriptionOperationResult<PlanResponse>.Success(
            _mapper.ToResponse(plan, prices),
            correlationId);
    }

    /// <summary>
    /// A plan scoped to another organization reports as missing rather than forbidden, so a
    /// response cannot be used to confirm that an identifier exists somewhere else.
    /// </summary>
    private static bool IsVisibleTo(Plan plan, string organizationId) =>
        plan.OrganizationId is null ||
        string.Equals(plan.OrganizationId, organizationId, StringComparison.Ordinal);

    private static bool QuantityItemExists(Plan plan, string? quantityItemKey) =>
        string.IsNullOrWhiteSpace(quantityItemKey) ||
        plan.QuantityItems.Exists(item =>
            string.Equals(item.ItemKey, quantityItemKey, StringComparison.Ordinal));

    private static SubscriptionOperationResult<PlanResponse> NotFound(string correlationId) =>
        SubscriptionOperationResult<PlanResponse>.Failure(
            PaymentFailureKind.NotFound,
            "subscription_plan_not_found",
            "The plan does not exist.",
            correlationId);

    private static Plan BuildPlan(CreatePlanRequest request, string tenantId) => new()
    {
        TenantId = tenantId,
        OrganizationId = string.IsNullOrWhiteSpace(request.OrganizationId)
            ? null
            : request.OrganizationId,
        Code = request.Code,
        DisplayName = request.DisplayName,
        Description = request.Description,
        FeaturesJson = request.FeaturesJson,
        Status = CatalogueStatus.Active,
        TrialDays = request.TrialDays,
        TrialRequiresPaymentMethod = request.TrialRequiresPaymentMethod,
        QuantityItems = request.QuantityItems
            .Select(item => new PlanQuantityItem
            {
                ItemKey = item.ItemKey,
                UnitLabel = item.UnitLabel,
                MinQuantity = item.MinQuantity,
                MaxQuantity = item.MaxQuantity,
                DefaultQuantity = item.DefaultQuantity
            })
            .ToList(),
        Meters = request.Meters
            .Select(meter => new PlanMeter
            {
                MeterKey = meter.MeterKey,
                DisplayName = meter.DisplayName,
                UnitLabel = meter.UnitLabel,
                Aggregation = meter.Aggregation,
                IncludedQuantity = meter.IncludedQuantity,
                OverageAllowed = meter.OverageAllowed,
                ThresholdPercents = meter.ThresholdPercents.Distinct().Order().ToList(),
                RateTables = meter.RateTables
                    .Select(table => new MeterRateTable
                    {
                        CurrencyCode = table.CurrencyCode.ToUpperInvariant(),
                        Tiers = table.Tiers
                            .Select(tier => new MeterTier
                            {
                                UpToQuantity = tier.UpToQuantity,
                                UnitAmountMinor = tier.UnitAmountMinor
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList(),
        Entitlements = request.Entitlements
            .Select(entitlement => new PlanEntitlement
            {
                Key = entitlement.Key,
                LimitKind = entitlement.LimitKind,
                Limit = entitlement.Limit,
                MeterKey = entitlement.MeterKey,
                UnitLabel = entitlement.UnitLabel
            })
            .ToList(),
        TrialGrants = request.TrialGrants
            .Select(grant => new TrialMeterGrant
            {
                MeterKey = grant.MeterKey,
                IncludedQuantity = grant.IncludedQuantity
            })
            .ToList()
    };
}
