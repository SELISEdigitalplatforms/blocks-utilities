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
    private readonly IPaymentMethodSetupService _paymentMethodSetups;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICurrencyMinorUnitResolver _currency;
    private readonly ILogger<SubscriptionCheckoutService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionCheckoutService(
        ISubscriptionCreationService creation,
        ISubscriptionRepository subscriptions,
        ISubscriptionPaymentLinkRepository links,
        ISubscriptionContextResolver contextResolver,
        ISubscriptionOutboxEventFactory events,
        ISubscriptionResponseMapper mapper,
        IPaymentService payments,
        IPaymentMethodSetupService paymentMethodSetups,
        IPaymentRepository paymentRepository,
        ICurrencyMinorUnitResolver currency,
        ILogger<SubscriptionCheckoutService> logger,
        ISubscriptionFinancialDocumentAnnouncer? documents = null,
        TimeProvider? time = null)
    {
        _creation = creation;
        _subscriptions = subscriptions;
        _links = links;
        _contextResolver = contextResolver;
        _events = events;
        _mapper = mapper;
        _payments = payments;
        _paymentMethodSetups = paymentMethodSetups;
        _paymentRepository = paymentRepository;
        _currency = currency;
        _logger = logger;
        _documents = documents;
        _time = time ?? TimeProvider.System;
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
        // the customer was quoted — and the same expression the purchase preview reports, so the
        // two cannot disagree.
        var amountMinor = SubscriptionAmountCalculator.InitialChargeAmountMinor(subscription);

        if (RequiresPayment(amountMinor))
        {
            return await ChargeAsync(subscription, amountMinor, correlationId, cancellationToken);
        }

        // Nothing is payable today. Whether that means the subscription starts now depends on
        // what the plan asked for: a card can be a condition of activation without being a
        // charge, and the two questions were conflated for as long as the only way to hold a
        // card was to take money with it.
        return SubscriptionAmountCalculator.RequiresCardSetup(subscription)
            ? await StartCardSetupAsync(subscription, correlationId, cancellationToken)
            : await StartWithoutPaymentAsync(
                subscription,
                context,
                correlationId,
                cancellationToken);
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

        var link = await _links.FindBySubscriptionAsync(
            context.TenantId,
            subscription.ItemId,
            cancellationToken);

        var checkoutUrl = await ResolveUsableCheckoutUrlAsync(
            context.TenantId,
            link,
            cancellationToken);

        if (!MatchesPendingTerms(request, subscription))
        {
            return PendingCheckoutConflict(subscription, checkoutUrl, correlationId);
        }

        if (string.IsNullOrWhiteSpace(checkoutUrl))
        {
            // A card-collection session that has expired is not a dead end. Nothing was paid, so
            // there is no money to reconcile and no reason to make someone cancel and start
            // again — but the expired session cannot be reopened, so the retry has to be a new
            // one under a new key.
            //
            // A *charge* is left alone. Raising a second one is how the same money gets taken
            // twice, and the existing conflict tells the caller to finish or cancel the first.
            return link is { Purpose: SubscriptionPaymentPurpose.PaymentMethodSetup }
                ? await RetryCardSetupAsync(subscription, link, correlationId, cancellationToken)
                : PendingCheckoutConflict(subscription, null, correlationId);
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
            _mapper.ToResponse(
                subscription,
                checkoutUrl,
                link is { Purpose: SubscriptionPaymentPurpose.PaymentMethodSetup }
                    ? PendingSetup("Pending", checkoutUrl)
                    : null),
            correlationId);
    }

    private async Task<string?> GetPendingCheckoutUrlAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken) =>
        await ResolveUsableCheckoutUrlAsync(
            tenantId,
            await _links.FindBySubscriptionAsync(tenantId, subscriptionId, cancellationToken),
            cancellationToken);

    private async Task<PendingCheckoutResponse?> GetPendingSetupAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        var link = await _links.FindBySubscriptionAsync(
            tenantId,
            subscriptionId,
            cancellationToken);

        if (link is not { Purpose: SubscriptionPaymentPurpose.PaymentMethodSetup })
        {
            return null;
        }

        var payment = await _paymentRepository.GetByIdAsync(
            tenantId,
            link.PaymentDetailId,
            cancellationToken);

        if (payment is null)
        {
            return PendingSetup("Failed", null, "payment_method_setup_not_found");
        }

        var expired = payment.ExpirationDate != default &&
                      payment.ExpirationDate <= DateTime.UtcNow;
        if (expired)
        {
            return PendingSetup("Expired", null, "payment_method_setup_expired");
        }

        if (payment.PaymentStatus is PaymentStatuses.Refused or
            PaymentStatuses.Cancelled or
            PaymentStatuses.MakePaymentFailed)
        {
            return PendingSetup(
                "Failed",
                null,
                "payment_method_setup_failed");
        }

        var url = link.State == SubscriptionPaymentLinkState.Pending
            ? await ResolveUsableCheckoutUrlAsync(tenantId, link, cancellationToken)
            : null;

        return link.State == SubscriptionPaymentLinkState.Pending && url is not null
            ? PendingSetup("Pending", url)
            : PendingSetup("Expired", null, "payment_method_setup_expired");
    }

    private static PendingCheckoutResponse PendingSetup(
        string state,
        string? checkoutUrl,
        string? errorCode = null) => new()
    {
        Purpose = nameof(SubscriptionPaymentPurpose.PaymentMethodSetup),
        State = state,
        ErrorCode = errorCode,
        CheckoutUrl = checkoutUrl
    };

    /// <summary>
    /// The hosted page this link still leads to, or null when there is nothing left to return to.
    /// </summary>
    private async Task<string?> ResolveUsableCheckoutUrlAsync(
        string tenantId,
        SubscriptionPaymentLink? link,
        CancellationToken cancellationToken)
    {
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
               payment.PaymentStatus is PaymentStatuses.Refused or
                   PaymentStatuses.Cancelled or
                   PaymentStatuses.MakePaymentFailed ||
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
            _time.GetUtcNow().UtcDateTime,
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
            var pendingSetup = await GetPendingSetupAsync(
                context.TenantId,
                subscription.ItemId,
                cancellationToken);

            var checkoutUrl = pendingSetup is not null
                ? pendingSetup.CheckoutUrl
                : await GetPendingCheckoutUrlAsync(
                    context.TenantId,
                    subscription.ItemId,
                    cancellationToken);

            return SubscriptionOperationResult<SubscriptionResponse>.Success(
                _mapper.ToResponse(subscription, checkoutUrl, pendingSetup),
                correlationId);
        }

        // No subscription is an answer, not a failure. This used to be a 404, which says the
        // endpoint is not there: a client cannot tell that from a bad route, a revoked path or a
        // typo, so every caller had to special-case one status code to read an ordinary "not yet".
        // The other not-found refusals in this module stay as they are - asking to cancel or reprice
        // a subscription that does not exist really is a request about something absent.
        return SubscriptionOperationResult<SubscriptionResponse>.Empty(correlationId);
    }

    /// <summary>
    /// Whether money has to move before this subscription grants anything.
    /// </summary>
    /// <remarks>
    /// A card-free trial and a fully discounted first period both come to nothing payable, and
    /// a zero-amount charge is not something the money path accepts — the currency resolver
    /// refuses anything at or below zero. So these start directly rather than being sent to a
    /// checkout that would decline them.
    /// <para>
    /// <paramref name="amountMinor"/> is already trial-aware — see
    /// <see cref="SubscriptionAmountCalculator.InitialChargeAmountMinor"/> — so the only question
    /// left here is whether it came to anything.
    /// </para>
    /// </remarks>
    private static bool RequiresPayment(long amountMinor) => amountMinor > 0;

    /// <summary>
    /// Opens a hosted session that stores a card and charges nothing.
    /// </summary>
    /// <remarks>
    /// The subscription stays <see cref="SubscriptionStatus.Incomplete"/> and grants nothing
    /// until the provider confirms the card was stored — the same rule a paid signup follows, and
    /// for the same reason: a browser that came back is not evidence, and the webhook is.
    /// </remarks>
    private async Task<SubscriptionOperationResult<SubscriptionResponse>> StartCardSetupAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var setup = await _paymentMethodSetups.CreateSetupAsync(
            new CreatePaymentMethodSetupRequest
            {
                ProviderName = PaymentConstants.StripeProvider,
                CurrencyCode = subscription.CurrencyCode,
                OrderId = subscription.OrderId,
                Description = $"{subscription.Plan.DisplayName} subscription",
                CustomerOrganizationId = subscription.OrganizationId
            },
            SubscriptionConstants.PaymentMethodSetupKeyFor(
                subscription.ItemId,
                subscription.PaymentMethodSetupAttempt),
            correlationId,
            cancellationToken);

        if (!setup.IsSuccess || setup.Payment is null)
        {
            _logger.LogWarning(
                "Subscription card setup failed TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Reason={Reason} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(setup.ErrorCode),
                correlationId);

            // Incomplete, granting nothing, and recoverable. No money moved, so there is nothing
            // to unwind and nothing that stops another attempt.
            return Failure(
                setup.FailureKind,
                setup.ErrorCode,
                setup.ErrorMessage,
                correlationId);
        }

        await _links.TryCreateAsync(
            new SubscriptionPaymentLink
            {
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                PaymentDetailId = setup.Payment.PaymentDetailId,
                OrderId = subscription.OrderId,
                Purpose = SubscriptionPaymentPurpose.PaymentMethodSetup,
                State = SubscriptionPaymentLinkState.Pending,
                CorrelationId = correlationId
            },
            cancellationToken);

        _logger.LogInformation(
            "Subscription card setup started TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} PaymentHash={PaymentHash} Attempt={Attempt} " +
            "CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Hash(setup.Payment.PaymentDetailId),
            subscription.PaymentMethodSetupAttempt,
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(
                subscription,
                setup.Payment.RedirectUrl,
                PendingSetup("Pending", setup.Payment.RedirectUrl)),
            correlationId);
    }

    /// <summary>
    /// Opens a fresh card-collection session after the last one expired.
    /// </summary>
    /// <remarks>
    /// The attempt counter is bumped first, and that write is the gate: it is a compare-and-set on
    /// the number itself, so two tabs retrying at once produce one new session and the loser is
    /// told the setup is already in progress rather than opening a third.
    /// </remarks>
    private async Task<SubscriptionOperationResult<SubscriptionResponse>> RetryCardSetupAsync(
        SubscriptionDetail subscription,
        SubscriptionPaymentLink link,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!await _subscriptions.TryBumpPaymentMethodSetupAttemptAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.PaymentMethodSetupAttempt,
                cancellationToken))
        {
            return PendingCheckoutConflict(subscription, null, correlationId);
        }

        // Nobody is going to finish the expired one, and a pending link is what the activation
        // sweep keeps coming back to. Settled after the bump: an abandoned link with no
        // replacement is recoverable, a second live session under a stale number is not.
        await _links.TrySettleAsync(
            subscription.TenantId,
            link.ItemId,
            SubscriptionPaymentLinkState.Abandoned,
            cancellationToken);

        subscription.PaymentMethodSetupAttempt++;

        return await StartCardSetupAsync(subscription, correlationId, cancellationToken);
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
        SubscriptionContext context,
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
            await _documents.AnnounceTrialAsync(
                subscription,
                correlationId,
                cancellationToken,
                SubscriptionDocumentSourceFactory.ActorOf(
                    context.UserId,
                    context.UserName,
                    context.UserEmail));
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
