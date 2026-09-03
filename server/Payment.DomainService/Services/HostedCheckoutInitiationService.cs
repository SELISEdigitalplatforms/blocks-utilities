using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class HostedCheckoutInitiationService : IPaymentInitiationService
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentProviderCache _providerCache;
    private readonly ICheckoutUrlPolicy _checkoutUrlPolicy;
    private readonly IProviderEndpointPolicyResolver _endpointPolicies;
    private readonly IPaymentSessionClientResolver _sessionClients;
    private readonly IPaymentStateTransitionService _stateTransitions;
    private readonly ICheckoutCallbackStateProtector _callbackStateProtector;
    private readonly IShopperReferenceService _shopperReferenceService;
    private readonly IPaymentWebhookReferenceService _webhookReferenceService;
    private readonly IStoredPaymentMethodRepository _storedPaymentMethods;
    private readonly IProviderInitiationRequestFactoryResolver _requestFactories;
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public HostedCheckoutInitiationService(
        IPaymentRepository repository,
        IPaymentProviderCache providerCache,
        ICheckoutUrlPolicy checkoutUrlPolicy,
        IProviderEndpointPolicyResolver endpointPolicies,
        IPaymentSessionClientResolver sessionClients,
        IPaymentStateTransitionService stateTransitions,
        ICheckoutCallbackStateProtector callbackStateProtector,
        IShopperReferenceService shopperReferenceService,
        IPaymentWebhookReferenceService webhookReferenceService,
        IStoredPaymentMethodRepository storedPaymentMethods,
        IProviderInitiationRequestFactoryResolver requestFactories,
        IOptionsMonitor<PaymentOptions> options)
    {
        _repository = repository;
        _providerCache = providerCache;
        _checkoutUrlPolicy = checkoutUrlPolicy;
        _endpointPolicies = endpointPolicies;
        _sessionClients = sessionClients;
        _stateTransitions = stateTransitions;
        _callbackStateProtector = callbackStateProtector;
        _shopperReferenceService = shopperReferenceService;
        _webhookReferenceService = webhookReferenceService;
        _storedPaymentMethods = storedPaymentMethods;
        _requestFactories = requestFactories;
        _options = options;
    }

    public async Task<PaymentOperationResult> InitiateAsync(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        PaymentDetail payment,
        string leaseId,
        long minorUnits,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // From the payment rather than the caller's context, so the recovery path below
        // resolves the very configuration this one did.
        var provider = await GetProviderAsync(
            payment.TenantId,
            payment.OrganizationId,
            request.ProviderName,
            cancellationToken);

        var providerFailure = await ValidateProviderAsync(
            provider,
            payment,
            leaseId,
            correlationId,
            cancellationToken);
        if (providerFailure != null) return providerFailure;

        // Fail closed, before anything is built and long before the provider is contacted, when
        // the caller already froze which exact PaymentProvider row this payment must resolve to
        // (see MakePaymentRequest.ExpectedProviderId). Resolving independently and comparing only
        // after a session already exists let a fallback to a shared configuration create a live
        // Adyen session and payment record with no way to link it back -- see PR #393's
        // subscription_payment_provider_scope_mismatch finding.
        var scopeMismatch = await ValidateExpectedProviderAsync(
            request.ExpectedProviderId,
            provider!,
            payment,
            leaseId,
            correlationId,
            cancellationToken);
        if (scopeMismatch != null) return scopeMismatch;

        var sessionClient = _sessionClients.Resolve(provider!.ProviderName);
        var requestFactory = _requestFactories.Resolve(provider.ProviderName);
        if (sessionClient == null || requestFactory == null)
        {
            return await _stateTransitions.CompleteFailureAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_provider_misconfigured",
                "The payment provider is temporarily unavailable.",
                correlationId,
                cancellationToken);
        }

        if (!_shopperReferenceService.TryCreate(
                payment.TenantId,
                context.ActorId,
                provider!.ShopperReferenceHmacKey ?? string.Empty,
                out var shopperReference))
        {
            return await _stateTransitions.CompleteFailureAsync(
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
            return await _stateTransitions.CompleteFailureAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_routing_unavailable",
                "The payment could not be routed safely.",
                correlationId,
                cancellationToken);
        }

        var hasUnresolvedRemoval =
            await _storedPaymentMethods.HasUnresolvedRemovalAsync(
                payment.TenantId,
                shopperReference,
                cancellationToken);

        if (hasUnresolvedRemoval &&
            request.ShouldSavePaymentMethod)
        {
            return await _stateTransitions.CompleteFailureAsync(
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
                TimeSpan.FromMinutes(Math.Clamp(_options.CurrentValue.CheckoutCallbackStateLifetimeMinutes, 5, 24 * 60)),
                provider.ReturnStateHmacKey ?? string.Empty);
        }
        catch (FormatException)
        {
            return await _stateTransitions.CompleteFailureAsync(
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
            return await _stateTransitions.CompleteFailureAsync(
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
            context,
            payment,
            provider,
            returnUrl,
            providerReference,
            shopperReference,
            await ResolveProviderPayerReferenceAsync(
                payment,
                shopperReference,
                provider.ProviderName,
                cancellationToken),
            includeStoredPaymentMethods: !hasUnresolvedRemoval,
            minorUnits);
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
            return PaymentOperationResult.Failure(
                PaymentFailureKind.Conflict,
                "payment_state_conflict",
                "The payment state changed while preparing the provider request.",
                correlationId);
        }

        var providerResult = await sessionClient.CreateSessionAsync(
            provider,
            providerRequest,
            payment.IdempotencyKey,
            cancellationToken);
        return await _stateTransitions.ApplyProviderResultAsync(
            payment,
            providerResult,
            leaseId,
            correlationId,
            cancellationToken);
    }

    public async Task RecoverAsync(PaymentDetail payment, CancellationToken cancellationToken)
    {
        if (payment.InitiationRequest == null) return;

        var leaseId = Guid.NewGuid().ToString("N");
        var leaseUntil = DateTime.UtcNow.AddSeconds(
            Math.Clamp(_options.CurrentValue.ProcessingLeaseSeconds, 10, 120));
        var claimed = await _repository.TryClaimInitiationAsync(
            payment.TenantId,
            payment.ItemId,
            leaseId,
            leaseUntil,
            cancellationToken);
        if (claimed == null) return;

        var provider = await GetProviderAsync(
            payment.TenantId,
            payment.OrganizationId,
            payment.ProviderName,
            cancellationToken);
        if (provider == null) return;

        // The same fail-closed guard InitiateAsync applies, for the same reason: a provider
        // configuration change between the original attempt and this recovery must not silently
        // retry the session against a different row than the one this payment was resolved
        // against (and, for a subscription, than the one frozen on its billing account).
        if (!string.IsNullOrWhiteSpace(payment.ResolvedProviderId) &&
            !string.Equals(payment.ResolvedProviderId, provider.ItemId, StringComparison.Ordinal))
        {
            return;
        }

        var sessionClient = _sessionClients.Resolve(provider.ProviderName);
        if (sessionClient == null) return;

        var providerResult = await sessionClient.CreateSessionAsync(
            provider,
            payment.InitiationRequest,
            payment.IdempotencyKey,
            cancellationToken);
        await _stateTransitions.ApplyProviderResultAsync(
            claimed,
            providerResult,
            leaseId,
            claimed.CorrelationId,
            cancellationToken);
    }

    private Task<PaymentProvider?> GetProviderAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        CancellationToken cancellationToken) =>
        _providerCache.GetAsync(
            tenantId,
            organizationId,
            providerName,
            () => _repository.GetProviderAsync(
                tenantId,
                organizationId,
                providerName,
                cancellationToken));

    private async Task<PaymentOperationResult?> ValidateProviderAsync(
        PaymentProvider? provider,
        PaymentDetail payment,
        string leaseId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (provider == null || !provider.IsEnabled)
        {
            return await _stateTransitions.CompleteFailureAsync(
                payment,
                leaseId,
                PaymentFailureKind.NotFound,
                "payment_provider_not_found",
                "The requested payment provider is unavailable.",
                correlationId,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(provider.ApiKey) || string.IsNullOrWhiteSpace(provider.MerchantId))
        {
            return await _stateTransitions.CompleteFailureAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_provider_misconfigured",
                "The payment provider is temporarily unavailable.",
                correlationId,
                cancellationToken);
        }


        if (!IsEndpointAllowed(provider) ||
            string.IsNullOrWhiteSpace(provider.ReturnStateHmacKey) ||
            string.IsNullOrWhiteSpace(provider.ShopperReferenceHmacKey))
        {
            return await _stateTransitions.CompleteFailureAsync(
                payment,
                leaseId,
                PaymentFailureKind.Unavailable,
                "payment_provider_misconfigured",
                "The payment provider is temporarily unavailable.",
                correlationId,
                cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Refuses to proceed when the caller froze an exact provider row and the scope-fallback
    /// chain resolved a different one.
    /// </summary>
    /// <remarks>
    /// Null <paramref name="expectedProviderId"/> means no caller has frozen an expectation for
    /// this payment, so every existing caller that never sets
    /// <see cref="Requests.MakePaymentRequest.ExpectedProviderId"/> is unaffected.
    /// </remarks>
    private async Task<PaymentOperationResult?> ValidateExpectedProviderAsync(
        string? expectedProviderId,
        PaymentProvider provider,
        PaymentDetail payment,
        string leaseId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedProviderId) ||
            string.Equals(expectedProviderId, provider.ItemId, StringComparison.Ordinal))
        {
            return null;
        }

        return await _stateTransitions.CompleteFailureAsync(
            payment,
            leaseId,
            PaymentFailureKind.Unavailable,
            "payment_provider_scope_mismatch",
            "The payment provider resolved a different configuration than the one this payment " +
                "was expected to use.",
            correlationId,
            cancellationToken);
    }

    /// <summary>
    /// The provider's own identifier for this shopper, taken from a card they saved earlier.
    /// </summary>
    /// <remarks>
    /// Only providers that mint their own payer identity record one, so this is null for the
    /// rest and for a shopper paying for the first time. Reusing it is what lets a returning
    /// shopper be recognised rather than treated as a new customer on every payment.
    /// </remarks>
    private async Task<string?> ResolveProviderPayerReferenceAsync(
        PaymentDetail payment,
        string shopperReference,
        string providerName,
        CancellationToken cancellationToken)
    {
        // Scoped to this payment's organization, which is what the card was stamped with when
        // it was saved. A payer identity minted for one organization means nothing to another,
        // and naming it there would attach this payment to a customer that has never been seen.
        //
        // Asked of every card the shopper has saved, not only the ones still active: removing
        // a card does not make its owner a different person. Reading only active cards meant a
        // shopper who removed their last one came back as a stranger and was given a second
        // provider customer — which is how a subscription's billing account ended up naming a
        // customer that no later payment would ever write to.
        //
        // Recognising a returning shopper is an improvement, not a requirement, so nothing
        // here may prevent a payment: an unresolved reference simply means a new customer.
        return await _storedPaymentMethods.FindProviderPayerReferenceAsync(
            payment.TenantId,
            [
                new StoredPaymentMethodLookupScope(
                    shopperReference,
                    payment.OrganizationId)
            ],
            providerName,
            cancellationToken);
    }

    /// <summary>
    /// Fails closed: a provider with no registered endpoint policy is never called.
    /// </summary>
    private bool IsEndpointAllowed(PaymentProvider provider) =>
        _endpointPolicies
            .Resolve(provider.ProviderName)?
            .IsAllowed(provider.ApiBaseUrl) == true;
}
