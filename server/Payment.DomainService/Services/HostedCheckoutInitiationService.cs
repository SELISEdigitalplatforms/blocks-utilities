using System.Text;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
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
    private readonly IPaymentSessionClient _sessionClient;
    private readonly IPaymentStateTransitionService _stateTransitions;
    private readonly ICheckoutCallbackStateProtector _callbackStateProtector;
    private readonly IShopperReferenceService _shopperReferenceService;
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public HostedCheckoutInitiationService(
        IPaymentRepository repository,
        IPaymentProviderCache providerCache,
        ICheckoutUrlPolicy checkoutUrlPolicy,
        IPaymentSessionClient sessionClient,
        IPaymentStateTransitionService stateTransitions,
        ICheckoutCallbackStateProtector callbackStateProtector,
        IShopperReferenceService shopperReferenceService,
        IOptionsMonitor<PaymentOptions> options)
    {
        _repository = repository;
        _providerCache = providerCache;
        _checkoutUrlPolicy = checkoutUrlPolicy;
        _sessionClient = sessionClient;
        _stateTransitions = stateTransitions;
        _callbackStateProtector = callbackStateProtector;
        _shopperReferenceService = shopperReferenceService;
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
        var provider = await GetProviderAsync(
            payment.TenantId,
            request.ProviderName,
            cancellationToken);

        var providerFailure = await ValidateProviderAsync(
            provider,
            payment,
            leaseId,
            correlationId,
            cancellationToken);
        if (providerFailure != null) return providerFailure;

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

        ProtectedCheckoutCallbackState protectedState;
        try
        {
            protectedState = _callbackStateProtector.Create(
                payment.TenantId,
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

        var providerRequest = BuildProviderRequest(
            request,
            context,
            payment,
            provider,
            returnUrl,
            shopperReference,
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
                cancellationToken))
        {
            return PaymentOperationResult.Failure(
                PaymentFailureKind.Conflict,
                "payment_state_conflict",
                "The payment state changed while preparing the provider request.",
                correlationId);
        }

        var providerResult = await _sessionClient.CreateSessionAsync(
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
            payment.ProviderName,
            cancellationToken);
        if (provider == null) return;

        var providerResult = await _sessionClient.CreateSessionAsync(
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
        string providerName,
        CancellationToken cancellationToken) =>
        _providerCache.GetAsync(
            tenantId,
            providerName,
            () => _repository.GetProviderAsync(tenantId, providerName, cancellationToken));

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


        if (!_checkoutUrlPolicy.IsAllowedProviderEndpoint(provider.ApiBaseUrl) ||
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

    private static HostedCheckoutSessionRequest BuildProviderRequest(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        PaymentDetail payment,
        PaymentProvider provider,
        string returnUrl,
        string shopperReference,
        long minorUnits) => new()
        {
            MerchantAccount = provider.MerchantId,
            Store = provider.StoreId,
            Amount = new ProviderAmount { Value = minorUnits, Currency = payment.CurrencyCode },
            ReturnUrl = returnUrl,
            Reference = payment.ItemId,
            Mode = "hosted",
            ThemeId = provider.ThemeId,
            CountryCode = provider.CountryCode ?? request.CustomerCountry ?? string.Empty,
            AdditionalData = new ProviderAdditionalData { ManualCapture = provider.ManualCapture },
            Metadata = new ProviderMetadata
            {
                TenantReference = Convert.ToBase64String(Encoding.UTF8.GetBytes(payment.TenantId)),
                SiteId = provider.SiteId,
                OrganizationId = context.OrganizationId
            },
            StorePaymentMethodMode = request.RememberCard ? "askForConsent" : "disabled",
            RecurringProcessingModel = request.RecurringModel,
            ShopperReference = shopperReference,
            ShopperEmail = request.CustomerEmail,
            ShopperInteraction = "Ecommerce"
        };
}
