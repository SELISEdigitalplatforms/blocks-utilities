using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class CheckoutObservationService : ICheckoutObservationService
{
    private readonly ICheckoutResultClientResolver _clients;
    private readonly ICheckoutResultValidator _resultValidator;
    private readonly ICheckoutStatusMapperResolver _statusMappers;
    private readonly IPaymentRepository _repository;
    private readonly ILogger<CheckoutObservationService> _logger;

    public CheckoutObservationService(
        ICheckoutResultClientResolver clients,
        ICheckoutResultValidator resultValidator,
        ICheckoutStatusMapperResolver statusMappers,
        IPaymentRepository repository,
        ILogger<CheckoutObservationService> logger)
    {
        _clients = clients;
        _resultValidator = resultValidator;
        _statusMappers = statusMappers;
        _repository = repository;
        _logger = logger;
    }

    public async Task<CheckoutObservationResult> ObserveAsync(
        CheckoutCallbackContext context,
        string sessionResult,
        CancellationToken cancellationToken)
    {
        var client = _clients.Resolve(context.Provider.ProviderName);

        if (client == null)
        {
            _logger.LogError(
                "No checkout result client is registered for the payment provider Provider={Provider}; falling back to the stored payment state",
                PaymentLogValue.Label(context.Provider.ProviderName));

            return await ResolveAuthoritativeStateAsync(
                context.Payment,
                fallbackToPending: true,
                cancellationToken: cancellationToken);
        }

        var providerResult = await client.GetAsync(
            context.Provider,
            context.Payment.SessionId!,
            sessionResult,
            cancellationToken);

        if (providerResult.Outcome == ProviderClientOutcome.Rejected)
        {
            return InvalidSessionResult();
        }

        if (providerResult.Outcome != ProviderClientOutcome.Success ||
            providerResult.Response == null)
        {
            return CheckoutObservationResult.Observed(PaymentRedirectStatuses.Pending);
        }

        var validationOutcome = _resultValidator.Validate(
            context.Payment,
            providerResult.Response);

        if (validationOutcome == CheckoutResultValidationOutcome.Mismatch)
        {
            return PaymentMismatch();
        }

        if (validationOutcome ==
            CheckoutResultValidationOutcome.ProviderDataUnavailable)
        {
            _logger.LogInformation(
                "Checkout result omitted payment amount Provider={Provider} Status={Status} PaymentCount={PaymentCount}; resolving authoritative payment state",
                context.Provider.ProviderName,
                providerResult.Response.Status,
                providerResult.Response.Payments?.Count ?? 0);

            return await ResolveAuthoritativeStateAsync(
                context.Payment,
                fallbackToPending: true,
                cancellationToken: cancellationToken);
        }

        var statusMapper = _statusMappers.Resolve(context.Provider.ProviderName);

        if (statusMapper == null)
        {
            _logger.LogError(
                "No checkout status mapper is registered for the payment provider Provider={Provider}; falling back to the stored payment state",
                PaymentLogValue.Label(context.Provider.ProviderName));

            return await ResolveAuthoritativeStateAsync(
                context.Payment,
                fallbackToPending: true,
                cancellationToken: cancellationToken);
        }

        return await SaveObservationAsync(
            statusMapper,
            context.Payment,
            providerResult.Response,
            sessionResult,
            cancellationToken);
    }

    private async Task<CheckoutObservationResult> SaveObservationAsync(
        ICheckoutStatusMapper statusMapper,
        PaymentDetail payment,
        HostedCheckoutResult checkoutResult,
        string sessionResult,
        CancellationToken cancellationToken)
    {
        var observedPayment = checkoutResult.Payments.FirstOrDefault();
        var normalizedStatus = statusMapper.Normalize(checkoutResult.Status!);
        var saved = await _repository.SaveCheckoutObservationAsync(
            payment.TenantId,
            payment.ItemId,
            normalizedStatus,
            observedPayment?.ResultCode,
            PaymentHashing.HashSensitiveValue(sessionResult),
            observedPayment?.PspReference,
            CreateInstrument(observedPayment),
            cancellationToken);

        if (!saved)
        {
            return await ResolveAuthoritativeStateAsync(
                payment,
                fallbackToPending: false,
                cancellationToken: cancellationToken);
        }

        return CheckoutObservationResult.Observed(
            statusMapper.ToRedirectStatus(normalizedStatus));
    }

    private async Task<CheckoutObservationResult> ResolveAuthoritativeStateAsync(
        PaymentDetail payment,
        bool fallbackToPending,
        CancellationToken cancellationToken)
    {
        var current = await _repository.GetByIdAsync(
            payment.TenantId,
            payment.ItemId,
            cancellationToken);

        return current?.PaymentStatus switch
        {
            PaymentStatuses.Authorized or
            PaymentStatuses.PartiallyCaptured or
            PaymentStatuses.Captured or
            PaymentStatuses.PartiallyRefunded or
            PaymentStatuses.Refunded =>
                CheckoutObservationResult.Observed(PaymentRedirectStatuses.Success),
            PaymentStatuses.Cancelled =>
                CheckoutObservationResult.Observed(
                    PaymentRedirectStatuses.Cancelled),
            PaymentStatuses.Refused =>
                CheckoutObservationResult.Observed(PaymentRedirectStatuses.Fail),
            _ when fallbackToPending =>
                CheckoutObservationResult.Observed(PaymentRedirectStatuses.Pending),
            _ => CheckoutObservationResult.Failed(
                CheckoutCallbackResult.Failure(
                    PaymentFailureKind.Unavailable,
                    "payment_observation_unavailable",
                    "The payment result could not be recorded."))
        };
    }

    private static PaymentInstrument? CreateInstrument(HostedCheckoutPayment? observedPayment) =>
        observedPayment?.PaymentMethod == null
            ? null
            : new PaymentInstrument
            {
                Type = observedPayment.PaymentMethod.Type,
                Brand = observedPayment.PaymentMethod.Brand
            };

    private static CheckoutObservationResult InvalidSessionResult() =>
        CheckoutObservationResult.Failed(
            CheckoutCallbackResult.Failure(
                PaymentFailureKind.Validation,
                "invalid_session_result",
                "The payment session result is invalid."));

    private static CheckoutObservationResult PaymentMismatch() =>
        CheckoutObservationResult.Failed(
            CheckoutCallbackResult.Failure(
                PaymentFailureKind.Validation,
                "payment_mismatch",
                "The payment result did not match the payment."));
}
