using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Moves a live subscription to a different price, mid-period.
/// </summary>
/// <remarks>
/// A trial has paid nothing yet, so its plan simply swaps. Otherwise the change is priced by
/// <see cref="SubscriptionProrationCalculator"/> and, if that leaves something owing, charged
/// immediately through the same <see cref="ISubscriptionBillingGateway"/> a renewal uses — this
/// is the seam's third caller, after the renewal service and (indirectly) the checkout service's
/// sibling money path.
/// <para>
/// Restricted to a same-currency, same-billing-interval target price. A different interval would
/// mean rebuilding the fee schedule and the current period boundaries mid-flight — a separate,
/// harder problem this does not attempt.
/// </para>
/// </remarks>
public sealed class SubscriptionPlanChangeService : ISubscriptionPlanChangeService
{
    private static readonly SubscriptionStatus[] EligibleStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active
    ];

    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly ISubscriptionResponseMapper _mapper;
    private readonly IValidator<ChangeSubscriptionPlanRequest> _validator;
    private readonly ILogger<SubscriptionPlanChangeService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionPlanChangeService(
        ISubscriptionContextResolver contextResolver,
        ISubscriptionRepository subscriptions,
        ISubscriptionCatalogueRepository catalogue,
        IBillingAccountRepository billingAccounts,
        ISubscriptionBillingGateway gateway,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        ISubscriptionResponseMapper mapper,
        IValidator<ChangeSubscriptionPlanRequest> validator,
        ILogger<SubscriptionPlanChangeService> logger,
        TimeProvider? time = null)
    {
        _contextResolver = contextResolver;
        _subscriptions = subscriptions;
        _catalogue = catalogue;
        _billingAccounts = billingAccounts;
        _gateway = gateway;
        _events = events;
        _cache = cache;
        _mapper = mapper;
        _validator = validator;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<SubscriptionResponse>> ChangePlanAsync(
        string subscriptionId,
        ChangeSubscriptionPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = await SubscriptionValidation
            .CheckAsync<ChangeSubscriptionPlanRequest, SubscriptionResponse>(
                _validator,
                request,
                "subscription_plan_change_invalid",
                "The plan change request is invalid.",
                correlationId,
                cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionResponse>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetAsync(
            context.TenantId,
            context.OrganizationId,
            subscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "The subscription does not exist.",
                correlationId);
        }

        if (!EligibleStatuses.Contains(subscription.Status))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_not_eligible",
                "This subscription cannot change plan in its current state.",
                correlationId);
        }

        var terms = await ResolveTargetAsync(request, context, subscription, correlationId, cancellationToken);

        if (!terms.IsSuccess)
        {
            return terms.ToFailure<SubscriptionResponse>();
        }

        var (plan, price) = terms.Value;
        var quantities = SubscriptionQuantityBuilder.Build(request.Quantities, plan, price);

        if (quantities is null)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_invalid",
                "The quantities do not match the plan's items or fall outside their bounds.",
                correlationId);
        }

        var newPlan = SubscriptionSnapshotBuilder.SnapshotOf(plan);
        var newPrice = SubscriptionSnapshotBuilder.SnapshotOf(price);

        return subscription.Status == SubscriptionStatus.Trialing
            ? await ApplyAsync(
                subscription, newPlan, newPrice, quantities,
                subscription.CreditBalanceMinor, null, correlationId, cancellationToken)
            : await ChargeAndApplyAsync(
                subscription, newPlan, newPrice, quantities, correlationId, cancellationToken);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ChargeAndApplyAsync(
        SubscriptionDetail subscription,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> quantities,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var outcome = SubscriptionProrationCalculator.Calculate(subscription, newPrice, quantities, now);

        if (outcome.ChargeMinor <= 0)
        {
            return await ApplyAsync(
                subscription, newPlan, newPrice, quantities,
                outcome.NewCreditBalanceMinor, null, correlationId, cancellationToken);
        }

        var account = await _billingAccounts.GetAsync(
            subscription.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_no_payment_method",
                "This upgrade cannot be charged without a saved payment method.",
                correlationId);
        }

        var charge = await _gateway.ChargeAsync(
            new SubscriptionChargeRequest
            {
                TenantId = subscription.TenantId,
                // The merchant's scope, not the subscriber's — see BillingAccount.
                OrganizationId =
                    account.ProviderOrganizationId ?? subscription.OrganizationId,
                ProviderName = account.ProviderName,
                StoredPaymentMethodId = account.DefaultPaymentMethodId,
                ProviderCustomerId = account.ProviderCustomerId,
                AmountMinor = outcome.ChargeMinor,
                CurrencyCode = subscription.CurrencyCode,
                OrderId = SubscriptionConstants.PlanChangeOrderIdFor(
                    subscription.ItemId,
                    subscription.Version),
                Description = $"{newPlan.DisplayName} plan change"
            },
            SubscriptionConstants.PlanChangeKeyFor(subscription.ItemId, subscription.Version),
            correlationId,
            cancellationToken);

        if (!charge.IsSuccess)
        {
            _logger.LogWarning(
                "Subscription plan change declined TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Reason={Reason}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(charge.ErrorCode ?? "unknown"));

            return charge.ToFailure<SubscriptionResponse>();
        }

        return await ApplyAsync(
            subscription, newPlan, newPrice, quantities,
            outcome.NewCreditBalanceMinor, charge.Value, correlationId, cancellationToken);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ApplyAsync(
        SubscriptionDetail subscription,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> quantities,
        long newCreditBalanceMinor,
        string? paymentDetailId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var previousPlanCode = subscription.Plan.Code;
        var expectedVersion = subscription.Version;

        subscription.Plan = newPlan;
        subscription.Price = newPrice;
        subscription.QuantityItems = quantities;
        subscription.CreditBalanceMinor = newCreditBalanceMinor;

        var applied = await _subscriptions.TryChangePlanAsync(
            subscription.TenantId,
            subscription.ItemId,
            expectedVersion,
            newPlan,
            newPrice,
            quantities,
            newCreditBalanceMinor,
            paymentDetailId,
            _events.CreatePlanChanged(subscription, previousPlanCode, correlationId),
            cancellationToken);

        if (!applied)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_conflict",
                "The subscription changed while this plan change was being applied.",
                correlationId);
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        _logger.LogInformation(
            "Subscription plan changed TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "PreviousPlan={PreviousPlan} NewPlan={NewPlan} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(previousPlanCode),
            PaymentLogValue.Label(newPlan.Code),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(subscription),
            correlationId);
    }

    private async Task<SubscriptionOperationResult<(Plan Plan, Price Price)>> ResolveTargetAsync(
        ChangeSubscriptionPlanRequest request,
        SubscriptionContext context,
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var plan = await _catalogue.FindPlanByCodeAsync(
            context.TenantId,
            context.OrganizationId,
            request.PlanCode,
            cancellationToken);

        if (plan is null)
        {
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_plan_not_found",
                "The plan does not exist or is not on sale.",
                correlationId);
        }

        var price = await _catalogue.GetPriceAsync(
            context.TenantId,
            request.PriceId,
            cancellationToken);

        if (price is null ||
            !string.Equals(price.PlanId, plan.ItemId, StringComparison.Ordinal) ||
            price.Status != CatalogueStatus.Active)
        {
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_price_not_found",
                "The price does not exist for this plan.",
                correlationId);
        }

        if (!string.Equals(price.CurrencyCode, subscription.CurrencyCode, StringComparison.Ordinal))
        {
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.Validation,
                "subscription_plan_change_currency_mismatch",
                "A subscription cannot change to a price in a different currency.",
                correlationId);
        }

        if (price.Interval != subscription.Price.Interval ||
            price.IntervalCount != subscription.Price.IntervalCount)
        {
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.Validation,
                "subscription_plan_change_interval_mismatch",
                "A subscription cannot change to a price with a different billing interval.",
                correlationId);
        }

        return SubscriptionOperationResult<(Plan, Price)>.Success((plan, price), correlationId);
    }

    private static SubscriptionOperationResult<SubscriptionResponse> Failure(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        SubscriptionOperationResult<SubscriptionResponse>.Failure(
            kind,
            errorCode,
            errorMessage,
            correlationId);
}
