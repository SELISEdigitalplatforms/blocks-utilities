using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
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
/// Subscribing, end to end: create the record, then take the money if there is any to take.
/// </summary>
/// <remarks>
/// Orchestration only. Building the subscription belongs to the creation service, computing the
/// amount to the calculator, and moving money to the payment module — this decides the order
/// and what to do when a step declines.
/// </remarks>
public sealed class SubscriptionCheckoutService : ISubscriptionCheckoutService
{
    private readonly ISubscriptionCreationService _creation;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionPaymentLinkRepository _links;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly ISubscriptionResponseMapper _mapper;
    private readonly IPaymentService _payments;
    private readonly ICurrencyMinorUnitResolver _currency;
    private readonly ILogger<SubscriptionCheckoutService> _logger;

    public SubscriptionCheckoutService(
        ISubscriptionCreationService creation,
        ISubscriptionRepository subscriptions,
        ISubscriptionPaymentLinkRepository links,
        ISubscriptionContextResolver contextResolver,
        ISubscriptionOutboxEventFactory events,
        ISubscriptionResponseMapper mapper,
        IPaymentService payments,
        ICurrencyMinorUnitResolver currency,
        ILogger<SubscriptionCheckoutService> logger)
    {
        _creation = creation;
        _subscriptions = subscriptions;
        _links = links;
        _contextResolver = contextResolver;
        _events = events;
        _mapper = mapper;
        _payments = payments;
        _currency = currency;
        _logger = logger;
    }

    public async Task<SubscriptionOperationResult<SubscriptionResponse>> SubscribeAsync(
        CreateSubscriptionRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionResponse>(correlationId);
        }

        var context = resolution.Context!;

        var created = await _creation.CreateAsync(
            request,
            context,
            correlationId,
            cancellationToken);

        if (!created.IsSuccess)
        {
            return created.ToFailure<SubscriptionResponse>();
        }

        var subscription = created.Value!;
        var amountMinor = SubscriptionAmountCalculator.PeriodAmountMinor(subscription);

        return RequiresPayment(subscription, amountMinor)
            ? await ChargeAsync(subscription, amountMinor, correlationId, cancellationToken)
            : await StartWithoutPaymentAsync(subscription, correlationId, cancellationToken);
    }

    public async Task<SubscriptionOperationResult<SubscriptionResponse>> GetCurrentAsync(
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
            return resolution.ToFailure<SubscriptionResponse>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetLiveAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        return subscription is null
            ? SubscriptionOperationResult<SubscriptionResponse>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "This organization has no active subscription.",
                correlationId)
            : SubscriptionOperationResult<SubscriptionResponse>.Success(
                _mapper.ToResponse(subscription),
                correlationId);
    }

    /// <summary>
    /// Whether money has to move before this subscription grants anything.
    /// </summary>
    /// <remarks>
    /// A card-free trial and a fully discounted first period both come to nothing payable, and
    /// a zero-amount charge is not something the money path accepts — the currency resolver
    /// refuses anything at or below zero. So these start directly rather than being sent to a
    /// checkout that would decline them.
    /// </remarks>
    private static bool RequiresPayment(SubscriptionDetail subscription, long amountMinor)
    {
        if (subscription.Trial is { RequiresPaymentMethod: false })
        {
            return false;
        }

        return amountMinor > 0;
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ChargeAsync(
        SubscriptionDetail subscription,
        long amountMinor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!_currency.TryConvertBack(
                amountMinor,
                subscription.CurrencyCode,
                out var amount))
        {
            return Failure(
                PaymentFailureKind.Unavailable,
                "subscription_currency_unsupported",
                "This currency is not configured for payments.",
                correlationId);
        }

        var payment = await _payments.MakePaymentAsync(
            new MakePaymentRequest
            {
                ProviderName = PaymentConstants.StripeProvider,
                Amount = amount,
                CurrencyCode = subscription.CurrencyCode,
                OrderId = subscription.OrderId,
                Description = $"{subscription.Plan.DisplayName} subscription",
                CustomerOrganizationId = subscription.OrganizationId,
                // The renewal in a month charges this card with nobody present, which the
                // provider only permits if the mandate was established when it was saved.
                SavePaymentMethod = true
            },
            // Derived from the subscription, so a retried request finds the same payment
            // instead of raising a second one — and so the recovery sweep can find it too.
            SubscriptionConstants.InitialChargeKeyFor(subscription.ItemId),
            correlationId,
            cancellationToken);

        if (!payment.IsSuccess || payment.Payment is null)
        {
            _logger.LogWarning(
                "Subscription initial charge failed TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Reason={Reason} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(payment.ErrorCode),
                correlationId);

            // The subscription stays incomplete and grants nothing. The customer can try
            // again from a clean state, and the recovery sweep tidies it if they do not.
            return Failure(
                payment.FailureKind,
                payment.ErrorCode,
                payment.ErrorMessage,
                correlationId);
        }

        await _links.TryCreateAsync(
            new SubscriptionPaymentLink
            {
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                PaymentDetailId = payment.Payment.PaymentDetailId,
                OrderId = subscription.OrderId,
                Purpose = SubscriptionPaymentPurpose.InitialCharge,
                State = SubscriptionPaymentLinkState.Pending,
                CorrelationId = correlationId
            },
            cancellationToken);

        _logger.LogInformation(
            "Subscription checkout started TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "PaymentHash={PaymentHash} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Hash(payment.Payment.PaymentDetailId),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(subscription, payment.Payment.RedirectUrl),
            correlationId);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> StartWithoutPaymentAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var target = subscription.Trial is null
            ? SubscriptionStatus.Active
            : SubscriptionStatus.Trialing;

        var eventType = target == SubscriptionStatus.Trialing
            ? SubscriptionConstants.SubscriptionTrialStarted
            : SubscriptionConstants.SubscriptionActivated;

        var applied = await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            new SubscriptionTransition(SubscriptionStatus.Incomplete, target)
            {
                ActivatedAtUtc = DateTime.UtcNow,
                Event = _events.Create(subscription, eventType, correlationId)
            },
            cancellationToken);

        if (!applied)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_transition_conflict",
                "The subscription changed while it was being started.",
                correlationId);
        }

        subscription.Status = target;

        _logger.LogInformation(
            "Subscription started without payment TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} Status={Status} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(target.ToString()),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(subscription),
            correlationId);
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
