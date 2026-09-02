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
    private readonly ISubscriptionAuditTrail _auditTrail;
    private readonly ILogger<PlanCatalogueService> _logger;

    public PlanCatalogueService(
        ISubscriptionCatalogueRepository catalogue,
        ISubscriptionRepository subscriptions,
        ISubscriptionContextResolver contextResolver,
        IValidator<CreatePlanRequest> planValidator,
        IValidator<UpdatePlanRequest> planUpdateValidator,
        IValidator<CreatePriceRequest> priceValidator,
        IPlanResponseMapper mapper,
        ISubscriptionAuditTrail auditTrail,
        ILogger<PlanCatalogueService> logger)
    {
        _catalogue = catalogue;
        _subscriptions = subscriptions;
        _contextResolver = contextResolver;
        _planValidator = planValidator;
        _planUpdateValidator = planUpdateValidator;
        _priceValidator = priceValidator;
        _mapper = mapper;
        _auditTrail = auditTrail;
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
            request.OrganizationId,
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
        // Null is the tenant-wide catalogue scope and must remain null. For an organization-scoped
        // plan, persist the resolver's answer rather than the caller's request: only the console may
        // name another organization, while every other caller is kept in the organization carried
        // by its authenticated context. Using the raw request here would let an untrusted caller
        // write a plan into somebody else's catalogue.
        plan.OrganizationId = string.IsNullOrWhiteSpace(request.OrganizationId)
            ? null
            : context.OrganizationId;

        string? predecessorDisplayName = null;

        if (!string.IsNullOrWhiteSpace(request.PredecessorPlanId))
        {
            var predecessor = await _catalogue.GetPlanAsync(
                context.TenantId,
                request.PredecessorPlanId,
                cancellationToken);

            // Checked once, here, so a stray or foreign id can never be stored — this is the
            // only validation the link ever gets. Not found and not visible are reported the
            // same way, for the same reason a plan lookup is elsewhere in this file: an
            // organization boundary must not be discoverable through what error comes back.
            if (predecessor is null || !IsVisibleTo(predecessor, context.OrganizationId))
            {
                return SubscriptionOperationResult<PlanResponse>.Failure(
                    PaymentFailureKind.Validation,
                    "subscription_plan_predecessor_not_found",
                    "The plan named as a predecessor does not exist, or is not visible here.",
                    correlationId);
            }

            plan.PredecessorPlanId = predecessor.ItemId;
            predecessorDisplayName = predecessor.DisplayName;
        }

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
            _mapper.ToResponse(plan, [], predecessorDisplayName: predecessorDisplayName),
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

        var archivedRefusal = RefuseIfArchived(plan, correlationId);

        if (archivedRefusal is not null)
        {
            return archivedRefusal;
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

    public async Task<SubscriptionOperationResult<PlanResponse>> ArchivePlanAsync(
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

        // Invisible and absent answer identically. An organization must not be able to learn that
        // another organization holds a plan by the difference between two error messages.
        if (plan is null || !IsVisibleTo(plan, context.OrganizationId))
        {
            await RecordArchiveAsync(
                context, planId, code: null, outcome: "NotFound", from: null, correlationId,
                cancellationToken);

            return NotFound(correlationId);
        }

        // Already done. Reported as success without a second write, so a retried request — a
        // double-clicked button, a client that did not see the first response — converges instead
        // of failing on work that has already happened.
        if (plan.Status == CatalogueStatus.Archived)
        {
            await RecordArchiveAsync(
                context, plan.ItemId, plan.Code, outcome: "AlreadyArchived",
                from: CatalogueStatus.Archived.ToString(), correlationId, cancellationToken);

            return await GetPlanAsync(
                plan.ItemId,
                context.OrganizationId,
                correlationId,
                cancellationToken);
        }

        // A draft was never on a menu, so there is nothing to take off one, and archiving is
        // permanent: it would strand the plan in a state it could never be sold from. Answered as
        // not found because that is what a draft is to every catalogue view.
        if (plan.Status != CatalogueStatus.Active)
        {
            await RecordArchiveAsync(
                context, plan.ItemId, plan.Code, outcome: "NotFound",
                from: plan.Status.ToString(), correlationId, cancellationToken);

            return NotFound(correlationId);
        }

        if (!await _catalogue.TryArchivePlanAsync(
                context.TenantId,
                plan.ItemId,
                plan.Version,
                DateTime.UtcNow,
                cancellationToken))
        {
            // The write was refused, and three different things cause that: somebody else archived
            // it, an unrelated edit moved the version on, or it is gone. Re-read to find out which,
            // because they need different answers and the write result cannot tell them apart.
            var current = await _catalogue.GetPlanAsync(
                context.TenantId,
                plan.ItemId,
                cancellationToken);

            if (current is null || !IsVisibleTo(current, context.OrganizationId))
            {
                await RecordArchiveAsync(
                    context, plan.ItemId, plan.Code, outcome: "NotFound",
                    from: plan.Status.ToString(), correlationId, cancellationToken);

                return NotFound(correlationId);
            }

            // Two archive requests raced and the other one won. Both callers wanted the same
            // end state and it is the state the plan is now in, so both are told it succeeded.
            if (current.Status == CatalogueStatus.Archived)
            {
                await RecordArchiveAsync(
                    context, plan.ItemId, plan.Code, outcome: "AlreadyArchived",
                    from: plan.Status.ToString(), correlationId, cancellationToken);

                return await GetPlanAsync(
                    plan.ItemId,
                    context.OrganizationId,
                    correlationId,
                    cancellationToken);
            }

            await RecordArchiveAsync(
                context, plan.ItemId, plan.Code, outcome: "Conflict",
                from: plan.Status.ToString(), correlationId, cancellationToken);

            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_changed",
                "This plan changed while you were archiving it. Reload it and try again.",
                correlationId);
        }

        await RecordArchiveAsync(
            context, plan.ItemId, plan.Code, outcome: "Changed",
            from: CatalogueStatus.Active.ToString(), correlationId, cancellationToken);

        _logger.LogInformation(
            "Subscription plan archived TenantHash={TenantHash} OrganizationHash={OrganizationHash} " +
            "PlanHash={PlanHash} Code={Code} Actor={Actor} Outcome={Outcome} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(context.OrganizationId),
            PaymentLogValue.Hash(plan.ItemId),
            PaymentLogValue.Label(plan.Code),
            PaymentLogValue.Hash(context.ActorId),
            "Changed",
            correlationId);

        return await GetPlanAsync(
            plan.ItemId,
            context.OrganizationId,
            correlationId,
            cancellationToken);
    }

    /// <summary>
    /// Records one archive attempt, whatever came of it.
    /// </summary>
    /// <remarks>
    /// Every outcome is written, including the ones that changed nothing. A refused or repeated
    /// attempt is exactly what somebody reading the trail later needs to see: it is the difference
    /// between a plan that was archived once and a client that has been retrying against a plan it
    /// cannot see.
    /// <para>
    /// The aggregate fields carry the plan rather than <c>SubscriptionId</c>, which stays null.
    /// Archiving a plan is the first audited decision with no subscription in it, and the
    /// subscribers holding a snapshot of that plan are precisely the ones it does not touch.
    /// </para>
    /// <para>
    /// Never allowed to fail the operation it describes. An audit write that throws must not turn
    /// a plan that really was archived into an error the caller retries, so the failure is logged
    /// and swallowed — the log line beside it carries the same facts.
    /// </para>
    /// </remarks>
    private async Task RecordArchiveAsync(
        SubscriptionContext context,
        string planId,
        string? code,
        string outcome,
        string? from,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditTrail.RecordAsync(
                new SubscriptionAuditEvent
                {
                    TenantId = context.TenantId,
                    OrganizationId = context.OrganizationId,
                    AggregateType = "Plan",
                    AggregateId = planId,
                    AggregateCode = code,
                    OperationId = correlationId,
                    CorrelationId = correlationId,
                    Operation = "PlanArchive",
                    Stage = "Catalogue",
                    Outcome = outcome,
                    Source = "Api",
                    ActorId = context.ActorId,
                    UserId = context.UserId,
                    FromStatus = from,
                    ToStatus = outcome is "Changed" or "AlreadyArchived"
                        ? CatalogueStatus.Archived.ToString()
                        : null,
                    OccurredAtUtc = DateTime.UtcNow
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Subscription plan archive audit write failed TenantHash={TenantHash} " +
                "PlanHash={PlanHash} Outcome={Outcome} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(context.TenantId),
                PaymentLogValue.Hash(planId),
                outcome,
                correlationId);
        }
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

        var archivedRefusal = RefuseIfArchived(plan, correlationId);

        if (archivedRefusal is not null)
        {
            return archivedRefusal;
        }

        if (!QuantityItemExists(plan, request.QuantityItemKey))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_item_unknown",
                "The price charges for a quantity item the plan does not define.",
                correlationId);
        }

        var stubBasis = await ResolveStubBasisAsync(
            request, plan, context, correlationId, cancellationToken);

        if (!stubBasis.IsSuccess)
        {
            return stubBasis.ToFailure<PlanResponse>();
        }

        var stubBasePrice = stubBasis.Value;

        var price = new Price
        {
            TenantId = context.TenantId,
            PlanId = plan.ItemId,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            // Authored, never derived. The linked monthly price prices the opening stub; what a
            // year costs is a separate commercial decision, and an annual plan is usually not
            // twelve monthly ones.
            UnitAmountMinor = request.UnitAmountMinor,
            Interval = request.Interval,
            IntervalCount = request.IntervalCount,
            BillingAlignment = request.BillingAlignment,
            DisplayPriceNote = request.DisplayPriceNote,
            QuantityItemKey = request.QuantityItemKey,
            TaxRateBasisPoints = request.TaxRateBasisPoints,
            TaxMode = request.TaxMode,
            AutomaticDiscountBasisPoints = request.AutomaticDiscountBasisPoints > 0
                ? request.AutomaticDiscountBasisPoints
                : null,
            QuantityDiscountCombination = request.AutomaticDiscountBasisPoints > 0
                ? request.QuantityDiscountCombination
                    ?? AutomaticDiscountCombination.BestDiscount
                : null,
            CalendarStubBasePriceId = stubBasePrice?.ItemId,
            // Copied, not referenced. Repricing or retiring the monthly price afterwards must not
            // change what this annual price is derived from, nor what a stub already sold on it
            // costs.
            CalendarStubBaseUnitAmountMinor = stubBasePrice?.UnitAmountMinor,
            CalendarAnnualChargeTiming = stubBasePrice is null
                ? CalendarAnnualChargeTiming.AtBoundary
                : request.CalendarAnnualChargeTiming ?? CalendarAnnualChargeTiming.AtBoundary,
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

    /// <summary>
    /// Finds and vets the monthly price a calendar-aligned yearly price is charged from.
    /// </summary>
    /// <remarks>
    /// Every check here exists because the stub is charged from this price's amount while the annual
    /// period is charged from the yearly price's own. A link to something on another plan, in
    /// another currency, or charging for a different quantity item would produce two figures a
    /// subscriber could not reconcile, and would only be discovered on an invoice.
    /// <para>
    /// Returns null, successfully, for every price that does not need one. The validator has
    /// already refused a link on a price that may not carry one.
    /// </para>
    /// </remarks>
    private async Task<SubscriptionOperationResult<Price?>> ResolveStubBasisAsync(
        CreatePriceRequest request,
        Plan plan,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CalendarStubBasePriceId))
        {
            return SubscriptionOperationResult<Price?>.Success(null, correlationId);
        }

        var basis = await _catalogue.GetPriceAsync(
            context.TenantId,
            request.CalendarStubBasePriceId,
            cancellationToken);

        SubscriptionOperationResult<Price?> Invalid(string message) =>
            SubscriptionOperationResult<Price?>.Failure(
                PaymentFailureKind.Validation,
                "subscription_calendar_stub_base_price_invalid",
                message,
                correlationId);

        if (basis is null ||
            !string.Equals(basis.PlanId, plan.ItemId, StringComparison.Ordinal))
        {
            return Invalid("The monthly price does not exist on this plan.");
        }

        if (basis.Status != CatalogueStatus.Active)
        {
            return Invalid("The monthly price is retired and cannot be charged from.");
        }

        if (basis.Interval != BillingInterval.Month || basis.IntervalCount != 1)
        {
            return Invalid("An annual price can only be charged from a price billed every month.");
        }

        if (!string.Equals(
                basis.CurrencyCode,
                request.CurrencyCode.ToUpperInvariant(),
                StringComparison.Ordinal))
        {
            return Invalid(
                "The monthly price is in another currency, and a subscription is billed in one.");
        }

        if (!string.Equals(
                basis.QuantityItemKey ?? string.Empty,
                request.QuantityItemKey ?? string.Empty,
                StringComparison.Ordinal))
        {
            return Invalid(
                "The monthly price charges for a different quantity item, so the two would " +
                "multiply by different things.");
        }

        // Tax has to agree in both rate and reading. A stub quoted inclusive and an annual period
        // quoted exclusive are two different prices to the customer for one subscription.
        if (basis.TaxRateBasisPoints != request.TaxRateBasisPoints ||
            (basis.TaxRateBasisPoints > 0 &&
             (basis.TaxMode ?? TaxMode.Exclusive) != (request.TaxMode ?? TaxMode.Exclusive)))
        {
            return Invalid("The monthly price is taxed differently from this one.");
        }

        return SubscriptionOperationResult<Price?>.Success(basis, correlationId);
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

        var archivedRefusal = RefuseIfArchived(plan, correlationId);

        if (archivedRefusal is not null)
        {
            return archivedRefusal;
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

    public async Task<SubscriptionOperationResult<PlanResponse>> UpdatePriceDiscountAsync(
        string priceId,
        UpdatePriceDiscountRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AutomaticDiscountBasisPoints is < 0 or > 10_000 ||
            request.QuantityDiscountCombination is { } combination &&
            !Enum.IsDefined(combination))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_price_discount_invalid",
                "An automatic discount must be between 0% and 100%, "
                    + "and combine with a volume band in a way this module knows.",
                correlationId);
        }

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);
        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<PlanResponse>(correlationId);
        }

        var context = resolution.Context!;
        var price = await _catalogue.GetPriceAsync(context.TenantId, priceId, cancellationToken);
        if (price is null)
        {
            return NotFound(correlationId);
        }

        var plan = await _catalogue.GetPlanAsync(context.TenantId, price.PlanId, cancellationToken);
        if (plan is null || !IsVisibleTo(plan, context.OrganizationId))
        {
            return NotFound(correlationId);
        }

        var archivedRefusal = RefuseIfArchived(plan, correlationId);

        if (archivedRefusal is not null)
        {
            return archivedRefusal;
        }

        // Zero and null are the same instruction — no automatic discount — and are stored the same
        // way, so a cleared discount reads back as absent rather than as a discount of nothing.
        var basisPoints = request.AutomaticDiscountBasisPoints > 0
            ? request.AutomaticDiscountBasisPoints
            : null;

        if (!await _catalogue.TryUpdatePriceAutomaticDiscountAsync(
                context.TenantId,
                priceId,
                price.Version,
                basisPoints,
                basisPoints > 0
                    ? request.QuantityDiscountCombination
                        ?? AutomaticDiscountCombination.BestDiscount
                    : null,
                DateTime.UtcNow,
                cancellationToken))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_price_discount_conflict",
                "The price changed while its automatic discount was being saved.",
                correlationId);
        }

        _logger.LogInformation(
            "Subscription price automatic discount updated TenantHash={TenantHash} "
                + "PriceHash={PriceHash} BasisPoints={BasisPoints} Combination={Combination} "
                + "CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(priceId),
            basisPoints ?? 0,
            PaymentLogValue.Label(
                (basisPoints > 0
                    ? request.QuantityDiscountCombination
                        ?? AutomaticDiscountCombination.BestDiscount
                    : AutomaticDiscountCombination.BestDiscount).ToString()),
            correlationId);

        return await GetPlanAsync(
            plan.ItemId,
            context.OrganizationId,
            correlationId,
            cancellationToken);
    }

    public async Task<SubscriptionOperationResult<PlanResponse>> UpdatePriceTaxAsync(
        string priceId,
        UpdatePriceTaxRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TaxRateBasisPoints is < 0 or > 10_000 ||
            request.TaxRateBasisPoints > 0 && request.TaxMode is null ||
            request.TaxMode is { } mode && !Enum.IsDefined(mode))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_price_tax_invalid",
                "Tax must be between 0% and 100%, with an inclusive or exclusive mode.",
                correlationId);
        }

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);
        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<PlanResponse>(correlationId);
        }

        var context = resolution.Context!;
        var price = await _catalogue.GetPriceAsync(context.TenantId, priceId, cancellationToken);
        if (price is null)
        {
            return NotFound(correlationId);
        }

        var plan = await _catalogue.GetPlanAsync(context.TenantId, price.PlanId, cancellationToken);
        if (plan is null || !IsVisibleTo(plan, context.OrganizationId))
        {
            return NotFound(correlationId);
        }

        var archivedRefusal = RefuseIfArchived(plan, correlationId);

        if (archivedRefusal is not null)
        {
            return archivedRefusal;
        }

        var rate = request.TaxRateBasisPoints > 0 ? request.TaxRateBasisPoints : null;
        var modeToStore = rate > 0 ? request.TaxMode : null;
        if (!await _catalogue.TryUpdatePriceTaxAsync(
                context.TenantId,
                priceId,
                price.Version,
                rate,
                modeToStore,
                DateTime.UtcNow,
                cancellationToken))
        {
            return SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_price_tax_conflict",
                "The price changed while its tax configuration was being saved.",
                correlationId);
        }

        return await GetPlanAsync(
            plan.ItemId,
            context.OrganizationId,
            correlationId,
            cancellationToken);
    }

    public async Task<SubscriptionOperationResult<IReadOnlyList<PlanResponse>>> ListPlansAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken,
        PlanCatalogueFilter filter = PlanCatalogueFilter.Active,
        string? familyCode = null)
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
            filter,
            cancellationToken,
            familyCode);

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

            var predecessorName = await ResolvePredecessorDisplayNameAsync(
                context.TenantId, plan.PredecessorPlanId, cancellationToken);

            // The reverse link is deliberately not resolved here: it would mean one extra
            // lookup per row just to render a list, for a fact each plan's own detail page
            // already reports.
            responses.Add(_mapper.ToResponse(
                plan, prices, hasSubscribers, predecessorDisplayName: predecessorName));
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

        var predecessorName = await ResolvePredecessorDisplayNameAsync(
            context.TenantId, plan.PredecessorPlanId, cancellationToken);

        var successor = await _catalogue.FindSuccessorPlanAsync(
            context.TenantId, plan.ItemId, cancellationToken);

        return SubscriptionOperationResult<PlanResponse>.Success(
            _mapper.ToResponse(
                plan,
                prices,
                hasSubscribers,
                predecessorDisplayName: predecessorName,
                successorPlanId: successor?.ItemId,
                successorDisplayName: successor?.DisplayName),
            correlationId);
    }

    /// <summary>
    /// Looks up a predecessor purely for display. No visibility check: the link was already
    /// validated as visible when it was created, and a predecessor's organization scope cannot
    /// change afterward, so re-checking here would only ever refuse something that was fine.
    /// </summary>
    private async Task<string?> ResolvePredecessorDisplayNameAsync(
        string tenantId, string? predecessorPlanId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(predecessorPlanId))
        {
            return null;
        }

        var predecessor = await _catalogue.GetPlanAsync(
            tenantId, predecessorPlanId, cancellationToken);

        return predecessor?.DisplayName;
    }

    /// <summary>
    /// A plan scoped to another organization reports as missing rather than forbidden, so a
    /// response cannot be used to confirm that an identifier exists somewhere else.
    /// </summary>
    /// <summary>
    /// Refuses any catalogue change to an archived plan, or null when the plan is still open.
    /// </summary>
    /// <remarks>
    /// One helper rather than the same three lines in five methods, because the set of operations
    /// this closes is the point: a plan that can no longer be sold must not be able to acquire a
    /// new price, a different tax rate or a fresh automatic discount either, since all three exist
    /// only to change what a future subscriber is charged and there will be no future subscriber.
    /// <para>
    /// Deliberately not applied to reading. Archived plans stay fully inspectable — that is what
    /// the Archived filter is for, and a plan somebody is deciding whether to duplicate is one
    /// they need to be able to open.
    /// </para>
    /// <para>
    /// Checked before the has-subscribers refusal in <c>UpdatePlanAsync</c>, and not after it:
    /// "create a new plan and move subscribers to it" is sound advice for a live plan and
    /// misleading for an archived one, where the answer is to duplicate it instead.
    /// </para>
    /// </remarks>
    private static SubscriptionOperationResult<PlanResponse>? RefuseIfArchived(
        Plan plan,
        string correlationId) =>
        plan.Status == CatalogueStatus.Archived
            ? SubscriptionOperationResult<PlanResponse>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_archived",
                "This plan is archived and can no longer be sold or changed.",
                correlationId)
            : null;

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
        // The validator refuses a request naming both, so exactly one of these two paths ever
        // has anything in it: TrialDays alone (legacy), or Kind/Count alone (current).
        TrialDurationKind = request.TrialDurationKind ?? TrialDurationKind.Days,
        TrialDurationCount = request.TrialDurationCount,
        TrialRequiresPaymentMethod = request.TrialRequiresPaymentMethod,
        RequirePaymentMethodUpfront = request.RequirePaymentMethodUpfront,
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
                QuantityScale = meter.QuantityScale,
                IncludedQuantity = meter.IncludedQuantity,
                // Was never copied here, so a carry-forward meter's cap was validated as
                // mandatory, reported by three responses as null, and read as "no cap" by
                // MeterAllowance.CarriedIn — the unbounded roll the validator's own message says
                // it prevents. Unrelated to fractional quantities; fixed here because this is the
                // initializer the scale had to be added to.
                CarryForwardCap = meter.CarryForwardCap,
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
