using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Opens a hosted session whose only purpose is to store a card.
/// </summary>
/// <remarks>
/// A near-twin of <see cref="HostedCheckoutInitiationService"/>, and deliberately not a branch
/// inside it. Everything that path does about money — the preflight that refuses a zero amount,
/// the currency conversion, the capture mode, the line item — is exactly what must not happen
/// here, and a flag threaded through all of it would leave the two behaviours interleaved in
/// code whose whole job is to be unambiguous about whether money moved.
/// <para>
/// What it does share is the provider machinery: the same lease, the same signed return state,
/// the same routing reference, the same session client, and the same state transitions. So a
/// setup session is recovered, resumed and confirmed by the code that already does those things.
/// </para>
/// </remarks>
public sealed class PaymentMethodSetupService : IPaymentMethodSetupService
{
    private readonly IPaymentExecutionContextResolver _contextResolver;
    private readonly IPaymentDistributedLock _distributedLock;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentProviderCache _providerCache;
    private readonly ICheckoutUrlPolicy _checkoutUrlPolicy;
    private readonly IProviderEndpointPolicyResolver _endpointPolicies;
    private readonly IPaymentSessionClientResolver _sessionClients;
    private readonly IPaymentMethodSetupRequestFactoryResolver _requestFactories;
    private readonly IPaymentStateTransitionService _stateTransitions;
    private readonly ICheckoutCallbackStateProtector _callbackStateProtector;
    private readonly IShopperReferenceService _shopperReferenceService;
    private readonly IPaymentWebhookReferenceService _webhookReferenceService;
    private readonly IStoredPaymentMethodRepository _storedPaymentMethods;
    private readonly IPaymentResponseMapper _responseMapper;
    private readonly IPaymentOrganizationResolver _organizationResolver;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentMethodSetupService> _logger;

    public PaymentMethodSetupService(
        IPaymentExecutionContextResolver contextResolver,
        IPaymentDistributedLock distributedLock,
        IPaymentRepository repository,
        IPaymentProviderCache providerCache,
        ICheckoutUrlPolicy checkoutUrlPolicy,
        IProviderEndpointPolicyResolver endpointPolicies,
        IPaymentSessionClientResolver sessionClients,
        IPaymentMethodSetupRequestFactoryResolver requestFactories,
        IPaymentStateTransitionService stateTransitions,
        ICheckoutCallbackStateProtector callbackStateProtector,
        IShopperReferenceService shopperReferenceService,
        IPaymentWebhookReferenceService webhookReferenceService,
        IStoredPaymentMethodRepository storedPaymentMethods,
        IPaymentResponseMapper responseMapper,
        IPaymentOrganizationResolver organizationResolver,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentMethodSetupService> logger)
    {
        _contextResolver = contextResolver;
        _distributedLock = distributedLock;
        _repository = repository;
        _providerCache = providerCache;
        _checkoutUrlPolicy = checkoutUrlPolicy;
        _endpointPolicies = endpointPolicies;
        _sessionClients = sessionClients;
        _requestFactories = requestFactories;
        _stateTransitions = stateTransitions;
        _callbackStateProtector = callbackStateProtector;
        _shopperReferenceService = shopperReferenceService;
        _webhookReferenceService = webhookReferenceService;
        _storedPaymentMethods = storedPaymentMethods;
        _responseMapper = responseMapper;
        _organizationResolver = organizationResolver;
        _options = options;
        _logger = logger;
    }

    public async Task<PaymentOperationResult> CreateSetupAsync(
        CreatePaymentMethodSetupRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextResolution = _contextResolver.Resolve(correlationId);
        if (!contextResolution.IsSuccess) return contextResolution.Failure!;

        var context = contextResolution.Context!;

        // Which organization the setup belongs to decides which merchant account the session --
        // and the token it stores -- is opened against, the same rule ReserveAsync's charge
        // counterpart already applies. Without this, a caller naming a scope on the request had
        // it silently ignored: CreateRecord always stamped the caller's own ambient organization.
        var organization = await _organizationResolver.ResolveAsync(
            request.OrganizationId,
            context,
            correlationId,
            cancellationToken);

        if (organization.Failure != null)
        {
            return organization.Failure;
        }

        await using var coordinationLock = await _distributedLock.TryAcquireAsync(
            PaymentHashing.CreateLockResource(context.TenantId, idempotencyKey),
            cancellationToken);

        var reserved = await ReserveAsync(
            request,
            context,
            organization.OrganizationId,
            idempotencyKey,
            correlationId,
            cancellationToken);

        if (reserved.Terminal is not null) return reserved.Terminal;

        return await InitiateAsync(
            request,
            context,
            reserved.Payment!,
            reserved.LeaseId!,
            correlationId,
            cancellationToken);
    }

    /// <summary>
    /// Claims the right to open a session for this key, or reports what the last attempt did.
    /// </summary>
    private async Task<(PaymentDetail? Payment, string? LeaseId, PaymentOperationResult? Terminal)>
        ReserveAsync(
            CreatePaymentMethodSetupRequest request,
            PaymentExecutionContext context,
            string? organizationId,
            string idempotencyKey,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var requestHash = PaymentHashing.CreateRequestHash(request);
        var leaseId = Guid.NewGuid().ToString("N");
        var leaseUntilUtc = DateTime.UtcNow.AddSeconds(
            Math.Clamp(_options.CurrentValue.ProcessingLeaseSeconds, 10, 120));

        var payment = CreateRecord(
            request,
            context,
            organizationId,
            idempotencyKey,
            correlationId,
            requestHash,
            leaseId,
            leaseUntilUtc);

        if (await _repository.TryCreateAsync(payment, cancellationToken))
        {
            return (payment, leaseId, null);
        }

        var existing = await _repository.GetByIdempotencyKeyAsync(
            context.TenantId,
            idempotencyKey,
            cancellationToken);

        if (existing is null)
        {
            return (null, null, Conflict(
                "payment_conflict",
                "The card setup could not be reserved.",
                correlationId));
        }

        if (!PaymentHashing.RequestHashesMatch(existing.RequestHash, requestHash) ||
            !string.Equals(
                existing.PaymentFlow,
                PaymentFlows.PaymentMethodSetup,
                StringComparison.Ordinal))
        {
            return (null, null, Conflict(
                "idempotency_key_reused",
                "The idempotency key was already used with a different request.",
                correlationId));
        }

        // Already open, or already done. Either way the caller gets the existing record: the
        // redirect URL on a live session is what lets someone resume a page they closed, and
        // returning a second session would strand the first one Stripe already accepted.
        if (existing.PaymentStatus is
            PaymentStatuses.Processing or
            PaymentStatuses.Authorized or
            PaymentStatuses.Captured)
        {
            return (null, null, PaymentOperationResult.Success(
                _responseMapper.Map(existing),
                correlationId,
                replay: true));
        }

        // A refused or failed attempt is terminal for *this* key. Reported rather than retried
        // under the same key, because the provider would replay the failure: a fresh attempt
        // needs a fresh key, and only the caller knows whether one is warranted.
        if (existing.PaymentStatus is
            PaymentStatuses.Refused or
            PaymentStatuses.Cancelled or
            PaymentStatuses.MakePaymentFailed)
        {
            return (null, null, PaymentOperationResult.Failure(
                PaymentFailureKind.ProviderRejected,
                existing.FailureCode ?? "payment_method_setup_failed",
                "The previous card setup attempt did not complete.",
                correlationId));
        }

        var claimed = await _repository.TryClaimInitiationAsync(
            context.TenantId,
            existing.ItemId,
            leaseId,
            leaseUntilUtc,
            cancellationToken);

        return claimed is null
            ? (null, null, Conflict(
                "payment_in_progress",
                "The card setup is already being started.",
                correlationId))
            : (claimed, leaseId, null);
    }

    private async Task<PaymentOperationResult> InitiateAsync(
        CreatePaymentMethodSetupRequest request,
        PaymentExecutionContext context,
        PaymentDetail payment,
        string leaseId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var provider = await _providerCache.GetAsync(
            payment.TenantId,
            payment.OrganizationId,
            request.ProviderName,
            () => _repository.GetProviderAsync(
                payment.TenantId,
                payment.OrganizationId,
                request.ProviderName,
                cancellationToken));

        if (provider is null || !provider.IsEnabled)
        {
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.NotFound,
                "payment_provider_not_found",
                "The requested payment provider is unavailable.",
                correlationId,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(provider.ApiKey) ||
            string.IsNullOrWhiteSpace(provider.MerchantId) ||
            string.IsNullOrWhiteSpace(provider.ReturnStateHmacKey) ||
            string.IsNullOrWhiteSpace(provider.ShopperReferenceHmacKey) ||
            _endpointPolicies.Resolve(provider.ProviderName)?.IsAllowed(provider.ApiBaseUrl) != true)
        {
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_provider_misconfigured",
                "The payment provider is temporarily unavailable.",
                correlationId,
                cancellationToken);
        }

        // Fail closed, before anything is built and long before the provider is contacted, when
        // the caller already froze which exact PaymentProvider row this setup must resolve to
        // (see CreatePaymentMethodSetupRequest.ExpectedProviderId). Resolving independently and
        // comparing only after a session already exists let a fallback to a shared configuration
        // create a live Adyen session with no way to link it back -- see PR #393's
        // subscription_payment_provider_scope_mismatch finding.
        if (!string.IsNullOrWhiteSpace(request.ExpectedProviderId) &&
            !string.Equals(request.ExpectedProviderId, provider.ItemId, StringComparison.Ordinal))
        {
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_provider_scope_mismatch",
                "The payment provider resolved a different configuration than the one this card " +
                    "setup was expected to use.",
                correlationId,
                cancellationToken);
        }

        var sessionClient = _sessionClients.Resolve(provider.ProviderName);
        var requestFactory = _requestFactories.Resolve(provider.ProviderName);

        if (sessionClient is null || requestFactory is null)
        {
            // Not every provider can hold a card without charging it. Reported as unsupported
            // rather than misconfigured: nothing is wrong with the configuration, this provider
            // simply cannot do the thing being asked of it.
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.Validation,
                "payment_method_setup_unsupported",
                "This payment provider cannot store a card without charging it.",
                correlationId,
                cancellationToken);
        }

        if (!_shopperReferenceService.TryCreate(
                payment.TenantId,
                context.ActorId,
                provider.ShopperReferenceHmacKey ?? string.Empty,
                out var shopperReference))
        {
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_provider_misconfigured",
                "The payment provider is temporarily unavailable.",
                correlationId,
                cancellationToken);
        }

        if (!_webhookReferenceService.TryCreate(
                payment.TenantId,
                payment.ItemId,
                out var providerReference))
        {
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_routing_unavailable",
                "The card setup could not be routed safely.",
                correlationId,
                cancellationToken);
        }

        // The whole point of this session is to leave a card behind, so an unresolved removal
        // blocks it outright rather than merely hiding the saved cards, exactly as it does for a
        // charge that asked to save one.
        if (await _storedPaymentMethods.HasUnresolvedRemovalAsync(
                payment.TenantId,
                shopperReference,
                cancellationToken))
        {
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.Conflict,
                "payment_method_removal_in_progress",
                "A stored payment method removal is still being confirmed.",
                correlationId,
                cancellationToken);
        }

        ProtectedCheckoutCallbackState protectedState;
        try
        {
            protectedState = _callbackStateProtector.Create(
                payment.TenantId,
                payment.OrganizationId,
                payment.ItemId,
                provider.ProviderName,
                TimeSpan.FromMinutes(Math.Clamp(
                    _options.CurrentValue.CheckoutCallbackStateLifetimeMinutes,
                    5,
                    24 * 60)),
                provider.ReturnStateHmacKey ?? string.Empty);
        }
        catch (FormatException)
        {
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_provider_misconfigured",
                "The payment provider is temporarily unavailable.",
                correlationId,
                cancellationToken);
        }

        if (!_checkoutUrlPolicy.TryResolveHostedUrls(
                provider,
                protectedState.Token,
                out var returnUrl,
                out var frontendResultUrl))
        {
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_provider_misconfigured",
                "The payment provider is temporarily unavailable.",
                correlationId,
                cancellationToken);
        }

        var providerRequest = requestFactory.Create(
            request,
            payment,
            provider,
            returnUrl,
            providerReference,
            shopperReference,
            await _storedPaymentMethods.FindProviderPayerReferenceAsync(
                payment.TenantId,
                [new StoredPaymentMethodLookupScope(shopperReference, payment.OrganizationId)],
                provider.ProviderName,
                cancellationToken));

        payment.InitiationRequest = providerRequest;

        if (!await _repository.SaveInitiationRequestAsync(
                payment.TenantId,
                payment.ItemId,
                leaseId,
                providerRequest,
                frontendResultUrl,
                PaymentHashing.HashSensitiveValue(protectedState.State.Nonce),
                shopperReference,
                cancellationToken,
                provider.ItemId,
                provider.OrganizationId))
        {
            return Conflict(
                "payment_state_conflict",
                "The card setup changed while the provider request was being prepared.",
                correlationId);
        }

        var providerResult = await sessionClient.CreateSessionAsync(
            provider,
            providerRequest,
            payment.IdempotencyKey,
            cancellationToken);

        var result = await _stateTransitions.ApplyProviderResultAsync(
            payment,
            providerResult,
            leaseId,
            correlationId,
            cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Card setup session opened TenantHash={TenantHash} PaymentHash={PaymentHash} " +
                "Provider={Provider} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(payment.TenantId),
                PaymentLogValue.Hash(payment.ItemId),
                PaymentLogValue.Label(provider.ProviderName),
                correlationId);
        }

        return result;
    }

    private PaymentDetail CreateRecord(
        CreatePaymentMethodSetupRequest request,
        PaymentExecutionContext context,
        string? organizationId,
        string idempotencyKey,
        string correlationId,
        string requestHash,
        string leaseId,
        DateTime leaseUntilUtc) =>
        new()
        {
            TenantId = context.TenantId,
            ProviderName = request.ProviderName.ToUpperInvariant(),
            PaymentStatus = PaymentStatuses.Initiating,
            PaymentFlow = PaymentFlows.PaymentMethodSetup,
            // Zero, and never anything else. Every reader that adds money up excludes this flow,
            // but a record that carried a plausible amount would be one careless join away from
            // being counted anyway.
            Amount = 0,
            PreciseAmount = 0,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            // The consent this session exists to obtain. Without it the webhook that reports the
            // stored card declines to record it, and the setup succeeds having saved nothing.
            RememberCard = true,
            // Resolved through the same authorization rule a charge uses -- see
            // IPaymentOrganizationResolver -- rather than the caller's ambient organization
            // unconditionally, so a setup session opens against the exact merchant scope the
            // caller asked for (and was allowed to ask for), not whichever organization happened
            // to be on the token making the internal call.
            OrganizationId = organizationId,
            CustomerOrganizationId = request.CustomerOrganizationId,
            CustomerEmail = request.CustomerEmail,
            UserId = context.UserId,
            OrderId = request.OrderId,
            Description = request.Description?.Trim(),
            TransactionId = Guid.NewGuid().ToString(),
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            CorrelationId = correlationId,
            ProcessingLeaseId = leaseId,
            ProcessingLeaseExpiresAtUtc = leaseUntilUtc,
            InitiationAttemptCount = 1,
            CreatedAtUtc = DateTime.UtcNow,
            LastUpdatedDateUtc = DateTime.UtcNow,
            PaymentDate = DateTime.UtcNow
        };

    private Task<PaymentOperationResult> FailAsync(
        PaymentDetail payment,
        string leaseId,
        PaymentFailureKind kind,
        string errorCode,
        string message,
        string correlationId,
        CancellationToken cancellationToken) =>
        _stateTransitions.CompleteFailureAsync(
            payment,
            leaseId,
            kind,
            errorCode,
            message,
            correlationId,
            cancellationToken);

    private static PaymentOperationResult Conflict(
        string code,
        string message,
        string correlationId) =>
        PaymentOperationResult.Failure(
            PaymentFailureKind.Conflict,
            code,
            message,
            correlationId);
}
