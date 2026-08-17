using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentProviderConfigurationService :
    IPaymentProviderConfigurationService
{
    private readonly IPaymentExecutionContextResolver _contextResolver;
    private readonly IValidator<UpdatePaymentProviderRequest> _validator;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentProviderCache _cache;
    private readonly IPaymentProviderResponseMapper _responseMapper;
    private readonly ILogger<PaymentProviderConfigurationService> _logger;

    public PaymentProviderConfigurationService(
        IPaymentExecutionContextResolver contextResolver,
        IValidator<UpdatePaymentProviderRequest> validator,
        IPaymentRepository repository,
        IPaymentProviderCache cache,
        IPaymentProviderResponseMapper responseMapper,
        ILogger<PaymentProviderConfigurationService> logger)
    {
        _contextResolver = contextResolver;
        _validator = validator;
        _repository = repository;
        _cache = cache;
        _responseMapper = responseMapper;
        _logger = logger;
    }

    public async Task<PaymentProviderMutationResult> UpdateAsync(
        string paymentProviderId,
        UpdatePaymentProviderRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextResolution = _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            return FromFailure(
                contextResolution.Failure!,
                correlationId);
        }

        if (string.IsNullOrWhiteSpace(paymentProviderId))
        {
            return ValidationFailure(
                "payment_provider_id_invalid",
                "A payment provider id is required.",
                correlationId);
        }

        var validation = await _validator.ValidateAsync(
            request,
            cancellationToken);

        if (!validation.IsValid)
        {
            var firstFailure = validation.Errors[0];

            return ValidationFailure(
                string.IsNullOrWhiteSpace(firstFailure.ErrorCode)
                    ? "payment_provider_request_invalid"
                    : firstFailure.ErrorCode,
                firstFailure.ErrorMessage,
                correlationId);
        }

        var tenantId = contextResolution.Context!.TenantId;
        Payment.DomainService.Entities.PaymentProvider? current;

        try
        {
            current = await _repository.GetProviderByIdAsync(
                tenantId,
                paymentProviderId,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return StoreUnavailable(
                exception,
                tenantId,
                correlationId);
        }

        if (current == null)
        {
            return NotFound(correlationId);
        }

        Payment.DomainService.Entities.PaymentProvider? updated;

        try
        {
            updated =
                await _repository.TryUpdateProviderConfigurationAsync(
                    tenantId,
                    paymentProviderId,
                    request.Version!.Value,
                    request.FrontendResultUrl,
                    Normalize(request.CountryCode)?.ToUpperInvariant(),
                    request.ManualCapture,
                    request.MaxRefundDays,
                    Normalize(request.StoreId),
                    request.IsEnabled,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return StoreUnavailable(
                exception,
                tenantId,
                correlationId);
        }

        if (updated == null)
        {
            return VersionConflict(correlationId);
        }

        await RefreshCacheAsync(
            tenantId,
            updated.OrganizationId,
            updated.ProviderName,
            updated.IsEnabled,
            cancellationToken);

        _logger.LogInformation(
            "Payment provider configuration updated Provider={Provider} TenantHash={TenantHash} Version={Version}",
            PaymentLogValue.Label(updated.ProviderName),
            PaymentLogValue.Hash(tenantId),
            updated.Version);

        return PaymentProviderMutationResult.Success(
            _responseMapper.Map(updated),
            correlationId);
    }

    private async Task RefreshCacheAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        bool expectAvailable,
        CancellationToken cancellationToken)
    {
        Payment.DomainService.Entities.PaymentProvider? refreshed;

        try
        {
            // Every organization's entry, not just this configuration's own. A tenant-level
            // configuration answers for every organization that has none of its own, so it is
            // cached under each of their keys, and evicting one would leave the rest serving
            // the configuration that was just changed.
            _cache.RemoveAll(tenantId, providerName);

            refreshed = await _cache.RefreshAsync(
                tenantId,
                organizationId,
                providerName,
                () => _repository.GetProviderAsync(
                    tenantId,
                    organizationId,
                    providerName,
                    cancellationToken));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Payment provider cache refresh failed after configuration update Provider={Provider} TenantHash={TenantHash}",
                PaymentLogValue.Label(providerName),
                PaymentLogValue.Hash(tenantId));

            return;
        }

        if (refreshed == null && expectAvailable)
        {
            _logger.LogError(
                "Payment provider cache refresh failed after configuration update Provider={Provider} TenantHash={TenantHash}",
                PaymentLogValue.Label(providerName),
                PaymentLogValue.Hash(tenantId));
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static PaymentProviderMutationResult FromFailure(
        PaymentOperationResult failure,
        string correlationId) =>
        PaymentProviderMutationResult.Failure(
            failure.FailureKind,
            failure.ErrorCode,
            failure.ErrorMessage,
            correlationId);

    private static PaymentProviderMutationResult ValidationFailure(
        string code,
        string message,
        string correlationId) =>
        PaymentProviderMutationResult.Failure(
            PaymentFailureKind.Validation,
            code,
            message,
            correlationId);

    private static PaymentProviderMutationResult NotFound(
        string correlationId) =>
        PaymentProviderMutationResult.Failure(
            PaymentFailureKind.NotFound,
            "payment_provider_not_found",
            "The payment provider was not found.",
            correlationId);

    private static PaymentProviderMutationResult VersionConflict(
        string correlationId) =>
        PaymentProviderMutationResult.Failure(
            PaymentFailureKind.Conflict,
            "payment_provider_version_conflict",
            "The payment provider was changed by another request. Reload it and retry.",
            correlationId);

    private PaymentProviderMutationResult StoreUnavailable(
        Exception exception,
        string tenantId,
        string correlationId)
    {
        _logger.LogError(
            exception,
            "Payment provider configuration persistence failed TenantHash={TenantHash}",
            PaymentLogValue.Hash(tenantId));

        return PaymentProviderMutationResult.Failure(
            PaymentFailureKind.Unavailable,
            "payment_provider_store_unavailable",
            "The payment provider could not be updated.",
            correlationId);
    }
}
