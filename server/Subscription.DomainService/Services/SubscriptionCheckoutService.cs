using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
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
    private readonly IPaymentRepository _paymentRepository;
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
        IPaymentRepository paymentRepository,
        ICurrencyMinorUnitResolver currency,
        ILogger<SubscriptionCheckoutService> logger,
        ISubscriptionFinancialDocumentAnnouncer? documents = null)
    {
        _creation = creation;
        _subscriptions = subscriptions;
        _links = links;
        _contextResolver = contextResolver;
        _events = events;
        _mapper = mapper;
        _payments = payments;
        _paymentRepository = paymentRepository;
        _currency = currency;
        _logger = logger;
        _documents = documents;
    }

    /// <summary>
    /// Optional so existing callers and tests compile unchanged. A card-free trial that starts
    /// without announcing its document is one the repair sweep has to find, not one that failed.
    /// </summary>
    private readonly ISubscriptionFinancialDocumentAnnouncer? _documents;

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
            if (string.Equals(
                    created.ErrorCode,
                    "subscription_already_active",
                    StringComparison.Ordinal))
            {
                var resumed = await TryResumeIncompleteCheckoutAsync(
                    request,
                    context,
                    correlationId,
                    cancellationToken);

                if (resumed is not null)
                {
                    return resumed;
                }
            }

            return created.ToFailure<SubscriptionResponse>();
        }

        var subscription = created.Value!;

        // The figure fixed when the subscription was built, so the charge raised here is the one
        // the customer was quoted. Falls back for the paths that never priced a first period —
        // a card-free trial, and any subscription written before the amount was frozen.
        var amountMinor = subscription.InitialChargeAmountMinor
            ?? SubscriptionAmountCalculator.PeriodAmountMinor(subscription);

        return RequiresPayment(subscription, amountMinor)
            ? await ChargeAsync(subscription, amountMinor, correlationId, cancellationToken)
            : await StartWithoutPaymentAsync(subscription, correlationId, cancellationToken);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>?> TryResumeIncompleteCheckoutAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetIncompleteAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        if (subscription is null)
        {
            // A live subscription caused the unique-index collision, or it moved state between
            // the insert and this read. Preserve the ordinary already-active response.
            return null;
        }

        var checkoutUrl = await GetPendingCheckoutUrlAsync(
            context.TenantId,
            subscription.ItemId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(checkoutUrl))
        {
            return PendingCheckoutConflict(subscription, null, correlationId);
        }

        if (!MatchesPendingTerms(request, subscription))
        {
            return PendingCheckoutConflict(subscription, checkoutUrl, correlationId);
        }

        _logger.LogInformation(
            "Existing subscription checkout resumed TenantHash={TenantHash} " +
            "OrganizationHash={OrganizationHash} SubscriptionHash={SubscriptionHash} " +
            "CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(context.OrganizationId),
            PaymentLogValue.Hash(subscription.ItemId),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(subscription, checkoutUrl),
            correlationId);
    }

    private async Task<string?> GetPendingCheckoutUrlAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        var link = await _links.FindBySubscriptionAsync(
            tenantId,
            subscriptionId,
            cancellationToken);

        if (link is null || link.State != SubscriptionPaymentLinkState.Pending)
        {
            return null;
        }

        // The link is already tenant- and organization-scoped. Read the linked payment directly:
        // PaymentService's caller-facing lookup scopes by the merchant OrganizationId, while a
        // subscription payment belongs to the subscriber through CustomerOrganizationId.
        var payment = await _paymentRepository.GetByIdAsync(
            tenantId,
            link.PaymentDetailId,
            cancellationToken);

        return payment is null ||
               string.IsNullOrWhiteSpace(payment.RedirectUrl) ||
               payment.ExpirationDate != default && payment.ExpirationDate <= DateTime.UtcNow
            ? null
            : payment.RedirectUrl;
    }

    private static bool MatchesPendingTerms(
        CreateSubscriptionRequest request,
        SubscriptionDetail subscription)
    {
        if (!string.Equals(request.PlanCode, subscription.Plan.Code, StringComparison.Ordinal) ||
            !string.Equals(request.PriceId, subscription.Price.PriceId, StringComparison.Ordinal) ||
            !string.Equals(
                request.DiscountCode?.Trim(),
                subscription.Discount?.Code,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                request.TimeZoneId,
                subscription.FeeSchedule.TimeZoneId,
                StringComparison.Ordinal))
        {
            return false;
        }

        return request.Quantities.Count == 0 || request.Quantities.All(requested =>
            subscription.QuantityItems.Any(existing =>
                string.Equals(existing.ItemKey, requested.ItemKey, StringComparison.Ordinal) &&
                existing.Quantity == requested.Quantity));
    }

    private static SubscriptionOperationResult<SubscriptionResponse> PendingCheckoutConflict(
        SubscriptionDetail subscription,
        string? checkoutUrl,
        string correlationId)
    {
        var details = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["subscriptionId"] = [subscription.ItemId]
        };

        if (!string.IsNullOrWhiteSpace(checkoutUrl))
        {
            details["checkoutUrl"] = [checkoutUrl];
        }

        return SubscriptionOperationResult<SubscriptionResponse>.Failure(
            PaymentFailureKind.Conflict,
            "subscription_checkout_pending",
            "This organization already has an unpaid checkout. Continue it or cancel it before choosing different terms.",
            correlationId,
            details);
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

        if (subscription is not null)
        {
            return SubscriptionOperationResult<SubscriptionResponse>.Success(
                _mapper.ToResponse(subscription),
                correlationId);
        }

        subscription = await _subscriptions.GetIncompleteAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        if (subscription is not null)
        {
            var checkoutUrl = await GetPendingCheckoutUrlAsync(
                context.TenantId,
                subscription.ItemId,
                cancellationToken);

            return SubscriptionOperationResult<SubscriptionResponse>.Success(
                _mapper.ToResponse(subscription, checkoutUrl),
                correlationId);
        }

        return SubscriptionOperationResult<SubscriptionResponse>.Failure(
            PaymentFailureKind.NotFound,
            "subscription_not_found",
            "This organization has no current or pending subscription.",
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

        if (target == SubscriptionStatus.Trialing && _documents is not null)
        {
            // The card-free trial. No money moved and there is nothing to invoice, but the
            // subscriber has entitlement they were granted on stated terms, and that is what the
            // zero-total trial invoice records.
            await _documents.AnnounceSubscriptionAsync(
                subscription,
                correlationId,
                cancellationToken);
        }

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
