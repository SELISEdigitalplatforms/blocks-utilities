using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class RecurringPaymentInitiationService :
    IRecurringPaymentInitiationService
{
    private readonly IPaymentRepository _payments;
    private readonly IStoredPaymentMethodRepository
        _storedPaymentMethods;
    private readonly IPaymentProviderCache _providers;
    private readonly IProviderTokenProtector _tokenProtector;
    private readonly IPaymentWebhookReferenceService
        _referenceService;
    private readonly IStoredPaymentChargeRequestFactory
        _requestFactory;
    private readonly IStoredPaymentChargeProviderGatewayResolver
        _gatewayResolver;
    private readonly IPaymentOutboxEventFactory _events;
    private readonly IPaymentStateTransitionService
        _stateTransitions;
    private readonly IPaymentResponseMapper _responseMapper;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentWorkDispatcher _workDispatcher;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<RecurringPaymentInitiationService>
        _logger;

    public RecurringPaymentInitiationService(
        IPaymentRepository payments,
        IStoredPaymentMethodRepository storedPaymentMethods,
        IPaymentProviderCache providers,
        IProviderTokenProtector tokenProtector,
        IPaymentWebhookReferenceService referenceService,
        IStoredPaymentChargeRequestFactory requestFactory,
        IStoredPaymentChargeProviderGatewayResolver gatewayResolver,
        IPaymentOutboxEventFactory events,
        IPaymentStateTransitionService stateTransitions,
        IPaymentResponseMapper responseMapper,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentWorkDispatcher workDispatcher,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<RecurringPaymentInitiationService> logger)
    {
        _payments = payments;
        _storedPaymentMethods = storedPaymentMethods;
        _providers = providers;
        _tokenProtector = tokenProtector;
        _referenceService = referenceService;
        _requestFactory = requestFactory;
        _gatewayResolver = gatewayResolver;
        _events = events;
        _stateTransitions = stateTransitions;
        _responseMapper = responseMapper;
        _minorUnits = minorUnits;
        _workDispatcher = workDispatcher;
        _options = options;
        _logger = logger;
    }

    public async Task<PaymentOperationResult> InitiateAsync(
        PaymentDetail payment,
        StoredPaymentMethod storedPaymentMethod,
        PaymentProvider provider,
        string leaseId,
        long minorUnits,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var methodClaimId = Guid.NewGuid().ToString("N");
        var claimedMethod =
            await _storedPaymentMethods.TryClaimForPaymentAsync(
                payment.TenantId,
                storedPaymentMethod.ItemId,
                payment.ShopperReference ?? string.Empty,
                methodClaimId,
                DateTime.UtcNow.AddSeconds(
                    Math.Clamp(
                        _options.CurrentValue
                            .ProviderTimeoutSeconds * 2,
                        30,
                        180)),
                cancellationToken);

        if (claimedMethod == null)
        {
            await _payments.MarkInitiationUnknownAsync(
                payment.TenantId,
                payment.ItemId,
                leaseId,
                "stored_payment_method_in_use",
                cancellationToken);

            await ScheduleRecoveryAsync(
                payment.TenantId,
                cancellationToken);

            return PaymentOperationResult.Failure(
                PaymentFailureKind.Conflict,
                "stored_payment_method_in_use",
                "The stored payment method is changing or already in use.",
                correlationId);
        }

        try
        {
            if (!_tokenProtector.TryUnprotect(
                    claimedMethod,
                    out var providerToken))
            {
                return await FailAsync(
                    payment,
                    leaseId,
                    PaymentFailureKind.Unavailable,
                    "stored_payment_method_token_unavailable",
                    "The stored payment method is temporarily unavailable.",
                    correlationId,
                    cancellationToken);
            }

            if (!_referenceService.TryCreate(
                    payment.TenantId,
                    payment.ItemId,
                    out var providerReference))
            {
                providerToken = string.Empty;

                return await FailAsync(
                    payment,
                    leaseId,
                    PaymentFailureKind.Unavailable,
                    "payment_reference_unavailable",
                    "The payment could not be prepared.",
                    correlationId,
                    cancellationToken);
            }

            var gateway = _gatewayResolver.Resolve(
                provider.ProviderName);

            if (gateway == null)
            {
                providerToken = string.Empty;

                return await FailAsync(
                    payment,
                    leaseId,
                    PaymentFailureKind.Unavailable,
                    "payment_provider_unavailable",
                    "The payment provider is temporarily unavailable.",
                    correlationId,
                    cancellationToken);
            }

            var providerRequest = _requestFactory.Create(
                payment,
                provider,
                providerReference,
                providerToken,
                minorUnits);
            providerToken = string.Empty;

            if (!await _payments.SaveProviderRoutingAsync(
                    payment.TenantId,
                    payment.ItemId,
                    leaseId,
                    providerReference,
                    provider.MerchantId,
                    cancellationToken))
            {
                return Conflict(correlationId);
            }

            var providerResult = await gateway.ChargeAsync(
                provider,
                providerRequest,
                payment.IdempotencyKey,
                cancellationToken);

            return await ApplyProviderResultAsync(
                payment,
                leaseId,
                providerResult,
                correlationId,
                cancellationToken);
        }
        finally
        {
            await _storedPaymentMethods.ReleasePaymentClaimAsync(
                payment.TenantId,
                claimedMethod.ItemId,
                methodClaimId,
                CancellationToken.None);
        }
    }

    public async Task RecoverAsync(
        PaymentDetail payment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                payment.StoredPaymentMethodPublicId) ||
            string.IsNullOrWhiteSpace(payment.ShopperReference) ||
            !_minorUnits.TryConvert(
                payment.PreciseAmount,
                payment.CurrencyCode,
                out var minorUnits))
        {
            return;
        }

        var leaseId = Guid.NewGuid().ToString("N");
        var leaseUntilUtc = DateTime.UtcNow.AddSeconds(
            Math.Clamp(
                _options.CurrentValue.ProcessingLeaseSeconds,
                10,
                120));
        var claimed =
            await _payments.TryClaimInitiationAsync(
                payment.TenantId,
                payment.ItemId,
                leaseId,
                leaseUntilUtc,
                cancellationToken);

        if (claimed == null)
        {
            return;
        }

        var provider = await _providers.GetAsync(
            claimed.TenantId,
            claimed.ProviderName,
            () => _payments.GetProviderAsync(
                claimed.TenantId,
                claimed.ProviderName,
                cancellationToken));
        var storedPaymentMethod =
            await _storedPaymentMethods.GetAsync(
                claimed.TenantId,
                claimed.StoredPaymentMethodPublicId!,
                cancellationToken);

        if (provider == null ||
            !provider.IsEnabled ||
            storedPaymentMethod == null)
        {
            await FailAsync(
                claimed,
                leaseId,
                PaymentFailureKind.Unavailable,
                "recurring_payment_recovery_unavailable",
                "The recurring payment could not be recovered.",
                claimed.CorrelationId,
                cancellationToken);

            return;
        }

        await InitiateAsync(
            claimed,
            storedPaymentMethod,
            provider,
            leaseId,
            minorUnits,
            claimed.CorrelationId,
            cancellationToken);
    }

    private async Task<PaymentOperationResult>
        ApplyProviderResultAsync(
            PaymentDetail payment,
            string leaseId,
            StoredPaymentChargeProviderResult providerResult,
            string correlationId,
            CancellationToken cancellationToken)
    {
        if (providerResult.Outcome ==
                StoredPaymentChargeOutcome.Accepted &&
            !string.IsNullOrWhiteSpace(
                providerResult.PspReference))
        {
            var outbox = _events.Create(
                payment,
                PaymentConstants.PaymentInitiated,
                PaymentStatuses.Processing);
            var updated =
                await _payments
                    .CompleteStoredPaymentChargeInitiationAsync(
                        payment.TenantId,
                        payment.ItemId,
                        leaseId,
                        providerResult.PspReference,
                        providerResult.ResultCode,
                        outbox,
                        cancellationToken);

            if (!updated)
            {
                return Conflict(correlationId);
            }

            await _workDispatcher.TryDispatchAsync(
                payment.TenantId,
                includeRecovery: false,
                cancellationToken: cancellationToken);

            payment.PaymentStatus =
                PaymentStatuses.Processing;
            payment.PspReference =
                providerResult.PspReference;
            payment.CheckoutResultCode =
                providerResult.ResultCode;

            _logger.LogInformation(
                "Recurring payment accepted PaymentIdHash={PaymentIdHash} TenantHash={TenantHash} Provider={Provider}",
                PaymentLogValue.Hash(payment.ItemId),
                PaymentLogValue.Hash(payment.TenantId),
                PaymentLogValue.Label(payment.ProviderName));

            return PaymentOperationResult.Success(
                _responseMapper.Map(payment),
                correlationId);
        }

        if (providerResult.Outcome ==
            StoredPaymentChargeOutcome.Rejected)
        {
            return await FailAsync(
                payment,
                leaseId,
                PaymentFailureKind.ProviderRejected,
                "recurring_payment_provider_rejected",
                "The payment provider rejected the recurring payment.",
                correlationId,
                cancellationToken);
        }

        var unavailable =
            providerResult.Outcome ==
            StoredPaymentChargeOutcome.Unavailable;
        var failureCode = unavailable
            ? "payment_provider_unavailable"
            : "recurring_payment_initiation_unknown";

        await _payments.MarkInitiationUnknownAsync(
            payment.TenantId,
            payment.ItemId,
            leaseId,
            failureCode,
            cancellationToken);

        await ScheduleRecoveryAsync(
            payment.TenantId,
            cancellationToken);

        return PaymentOperationResult.Failure(
            unavailable
                ? PaymentFailureKind.Unavailable
                : providerResult.Outcome ==
                  StoredPaymentChargeOutcome.Timeout
                    ? PaymentFailureKind.Timeout
                    : PaymentFailureKind.ProviderFailure,
            failureCode,
            unavailable
                ? "The payment provider is temporarily unavailable."
                : "The provider outcome is unknown. Retry with the same idempotency key.",
            correlationId);
    }

    private Task<PaymentOperationResult> FailAsync(
        PaymentDetail payment,
        string leaseId,
        PaymentFailureKind failureKind,
        string failureCode,
        string safeMessage,
        string correlationId,
        CancellationToken cancellationToken) =>
        _stateTransitions.CompleteFailureAsync(
            payment,
            leaseId,
            failureKind,
            failureCode,
            safeMessage,
            correlationId,
            cancellationToken);

    private Task<bool> ScheduleRecoveryAsync(
        string tenantId,
        CancellationToken cancellationToken) =>
        _workDispatcher.TryDispatchAsync(
            tenantId,
            includeRecovery: true,
            scheduledAtUtc:
                DateTimeOffset.UtcNow.AddSeconds(30),
            cancellationToken: cancellationToken);

    private static PaymentOperationResult Conflict(
        string correlationId) =>
        PaymentOperationResult.Failure(
            PaymentFailureKind.Conflict,
            "payment_state_conflict",
            "The payment state changed while processing.",
            correlationId);
}
