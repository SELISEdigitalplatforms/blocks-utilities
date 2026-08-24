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
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IValidator<CreatePlanRequest> _planValidator;
    private readonly IValidator<UpdatePlanRequest> _planUpdateValidator;
    private readonly IValidator<CreatePriceRequest> _priceValidator;
    private readonly IPlanResponseMapper _mapper;
    private readonly ILogger<PlanCatalogueService> _logger;

    public PlanCatalogueService(
        ISubscriptionCatalogueRepository catalogue,
        ISubscriptionRepository subscriptions,
        ISubscriptionContextResolver contextResolver,
        IValidator<CreatePlanRequest> planValidator,
        IValidator<UpdatePlanRequest> planUpdateValidator,
        IValidator<CreatePriceRequest> priceValidator,
        IPlanResponseMapper mapper,
        ILogger<PlanCatalogueService> logger)
    {
        _catalogue = catalogue;
        _subscriptions = subscriptions;
        _contextResolver = contextResolver;
        _planValidator = planValidator;
        _planUpdateValidator = planUpdateValidator;
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

        plan.Code = request.Code;
        plan.OrganizationId = string.IsNullOrWhiteSpace(request.OrganizationId)
            ? null
            : request.OrganizationId;

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

    public async Task<SubscriptionOperationResult<PlanResponse>> UpdatePlanAsync(
        string planId,
        UpdatePlanRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<PlanResponse>(correlationId);
        }

        var invalid = await SubscriptionValidation.CheckAsync<UpdatePlanRequest, PlanResponse>(
            _planUpdateValidator,
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
        var plan = await _catalogue.GetPlanAsync(
            context.TenantId,
            planId,
            cancellationToken);

        if (plan is null || !IsVisibleTo(plan, context.OrganizationId))
        {
            return NotFound(correlationId);
        }

        if (await _subscriptions.AnySubscriberAsync(context.TenantId, plan.ItemId, cancellationToken))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_in_use",
                "This plan has been subscribed to, so its terms can no longer be changed. " +
                "Create a new plan instead and move subscribers to it.",
                correlationId);
        }

        // The plan's own identity survives: only what it sells is rewritten. Code and
        // organization come from the stored plan rather than the request, which cannot name them.
        var edited = BuildPlan(request, context.TenantId);
        edited.Code = plan.Code;
        edited.OrganizationId = plan.OrganizationId;

        // Guarded by the version just read: a second edit landing in between moves it on, and
        // this one is refused rather than overwriting what it never saw.
        if (!await _catalogue.TryUpdatePlanAsync(
                context.TenantId,
                plan.ItemId,
                plan.Version,
                edited,
                cancellationToken))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_changed",
                "This plan changed while you were editing it. Reload it and apply your changes " +
                "again.",
                correlationId);
        }

        _logger.LogInformation(
            "Subscription plan updated TenantHash={TenantHash} PlanHash={PlanHash} Code={Code} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(plan.ItemId),
            PaymentLogValue.Label(plan.Code),
            correlationId);

        return await GetPlanAsync(
            plan.ItemId,
            context.OrganizationId,
            correlationId,
            cancellationToken);
    }

    public async Task<SubscriptionOperationResult<PlanResponse>> CreatePriceAsync(
        CreatePriceRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<PlanResponse>(correlationId);
        }

        var priceInvalid = await SubscriptionValidation.CheckAsync<CreatePriceRequest, PlanResponse>(
            _priceValidator,
            request,
            "subscription_price_invalid",
            "The price is invalid.",
            correlationId,
            cancellationToken);

        if (priceInvalid is not null)
        {
            return priceInvalid;
        }

        var context = resolution.Context!;
        var plan = await _catalogue.GetPlanAsync(
            context.TenantId,
            request.PlanId,
            cancellationToken);

        // Visibility is checked here as it is on a read: the lookup above is keyed only by
        // tenant and plan, so without this any caller in the tenant could put a price on
        // another organization's plan.
        if (plan is null || !IsVisibleTo(plan, context.OrganizationId))
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
            DisplayPriceNote = request.DisplayPriceNote,
            QuantityItemKey = request.QuantityItemKey,
            TaxRateBasisPoints = request.TaxRateBasisPoints,
            TaxMode = request.TaxMode,
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

        // The resolved organization, which is the plan's own once the request named it. Reading
        // back under the caller's unresolved scope reported the plan as missing — after the
        // price had already been committed, so the caller saw a failure for work that landed.
        return await GetPlanAsync(
            plan.ItemId,
            context.OrganizationId,
            correlationId,
            cancellationToken);
    }

    public async Task<SubscriptionOperationResult<PlanResponse>> ArchivePriceAsync(
        string priceId,
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
        var price = await _catalogue.GetPriceAsync(
            context.TenantId,
            priceId,
            cancellationToken);

        if (price is null)
        {
            return NotFound(correlationId);
        }

        var plan = await _catalogue.GetPlanAsync(
            context.TenantId,
            price.PlanId,
            cancellationToken);

        // The same visibility check creating a price makes, and for the same reason: the price
        // lookup above is keyed only by tenant, so without this any caller in the tenant could
        // retire a price on another organization's plan.
        if (plan is null || !IsVisibleTo(plan, context.OrganizationId))
        {
            return NotFound(correlationId);
        }

        if (!await _catalogue.TryArchivePriceAsync(
                context.TenantId,
                priceId,
                DateTime.UtcNow,
                cancellationToken))
        {
            // Already archived, or never active. Reported rather than treated as success, so a
            // second click does not read as having retired something a moment ago.
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_price_not_active",
                "This price is not on the menu, so it cannot be taken off it.",
                correlationId);
        }

        _logger.LogInformation(
            "Subscription price archived TenantHash={TenantHash} PlanHash={PlanHash} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(plan.ItemId),
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

            var hasSubscribers = await _subscriptions.AnySubscriberAsync(
                context.TenantId,
                plan.ItemId,
                cancellationToken);

            responses.Add(_mapper.ToResponse(plan, prices, hasSubscribers));
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

        var hasSubscribers = await _subscriptions.AnySubscriberAsync(
            context.TenantId,
            plan.ItemId,
            cancellationToken);

        return SubscriptionOperationResult<PlanResponse>.Success(
            _mapper.ToResponse(plan, prices, hasSubscribers),
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

    /// <summary>
    /// The parts of a plan that come from the request. Creating fills in the code and scope
    /// afterwards; editing copies them off the stored plan, which is what keeps an edit from
    /// moving a plan out from under the organization that can see it.
    /// </summary>
    private static Plan BuildPlan(PlanDefinitionRequest request, string tenantId) => new()
    {
        TenantId = tenantId,
        DisplayName = request.DisplayName,
        Description = request.Description,
        FamilyCode = request.FamilyCode,
        FamilyRank = request.FamilyRank,
        UsageInterval = request.UsageInterval,
        UsageIntervalCount = request.UsageIntervalCount,
        QuantityDiscountCombinationPolicy = request.QuantityDiscountCombinationPolicy,
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
                DefaultQuantity = item.DefaultQuantity,
                QuantityDiscountTiers = item.QuantityDiscountTiers
                    .OrderBy(tier => tier.MinimumQuantity)
                    .Select(tier => new QuantityDiscountTier
                    {
                        MinimumQuantity = tier.MinimumQuantity,
                        MaximumQuantity = tier.MaximumQuantity,
                        DiscountBasisPoints = tier.DiscountBasisPoints
                    })
                    .ToList()
            })
            .ToList(),
        Meters = request.Meters
            .Select(meter => new PlanMeter
            {
                MeterKey = meter.MeterKey,
                DisplayName = meter.DisplayName,
                UnitLabel = meter.UnitLabel,
                Aggregation = meter.Aggregation,
                ResetPolicy = meter.ResetPolicy,
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
