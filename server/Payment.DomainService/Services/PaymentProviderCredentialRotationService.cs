using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentProviderCredentialRotationService :
    IPaymentProviderCredentialRotationService
{
    private readonly IPaymentExecutionContextResolver _contextResolver;
    private readonly IValidator<RotatePaymentProviderCredentialsRequest>
        _validator;
    private readonly IReadOnlyCollection<
        IProviderCredentialRotationStrategy> _strategies;
    private readonly IAesGcmSecretProtector _protector;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentProviderCache _cache;
    private readonly IPaymentProviderResponseMapper _responseMapper;
    private readonly ILogger<PaymentProviderCredentialRotationService>
        _logger;

    public PaymentProviderCredentialRotationService(
        IPaymentExecutionContextResolver contextResolver,
        IValidator<RotatePaymentProviderCredentialsRequest> validator,
        IEnumerable<IProviderCredentialRotationStrategy> strategies,
        IAesGcmSecretProtector protector,
        IPaymentRepository repository,
        IPaymentProviderCache cache,
        IPaymentProviderResponseMapper responseMapper,
        ILogger<PaymentProviderCredentialRotationService> logger)
    {
        _contextResolver = contextResolver;
        _validator = validator;
        _strategies = strategies.ToArray();
        _protector = protector;
        _repository = repository;
        _cache = cache;
        _responseMapper = responseMapper;
        _logger = logger;
    }

    public async Task<PaymentProviderMutationResult> RotateAsync(
        string paymentProviderId,
        RotatePaymentProviderCredentialsRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextResolution = _contextResolver.Resolve(correlationId);

        if (!contextResolution.IsSuccess)
        {
            var failure = contextResolution.Failure!;

            return PaymentProviderMutationResult.Failure(
                failure.FailureKind,
                failure.ErrorCode,
                failure.ErrorMessage,
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
                    ? "payment_provider_rotation_invalid"
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
            return PaymentProviderMutationResult.Failure(
                PaymentFailureKind.NotFound,
                "payment_provider_not_found",
                "The payment provider was not found.",
                correlationId);
        }

        var strategy = _strategies.FirstOrDefault(candidate =>
            candidate.Supports(current.ProviderName));

        if (strategy == null)
        {
            return PaymentProviderMutationResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_provider_rotation_unsupported",
                "Credential rotation is not available for this provider.",
                correlationId);
        }

        var plan = await strategy.CreatePlanAsync(
            current,
            request,
            cancellationToken);

        if (!plan.IsSuccess)
        {
            return PaymentProviderMutationResult.Failure(
                plan.FailureKind,
                plan.ErrorCode,
                plan.ErrorMessage,
                correlationId);
        }

        // Re-encrypted under the provider's own key ring, which is the one the blobs were read
        // with — rotating credentials must not silently move them onto a different scope's key.
        var scope = PaymentEncryptionScope.From(current);
        var providerProtection = await _protector.ProtectAsync(
            scope,
            plan.CredentialJson,
            cancellationToken);
        var tenantProtection = await _protector.ProtectAsync(
            scope,
            plan.TenantSecurityJson,
            cancellationToken);
        var providerCiphertext = providerProtection.Ciphertext;
        var providerKeyId = providerProtection.KeyId;
        var tenantCiphertext = tenantProtection.Ciphertext;

        if (!providerProtection.IsProtected ||
            !tenantProtection.IsProtected ||
            !string.Equals(
                providerKeyId,
                tenantProtection.KeyId,
                StringComparison.Ordinal))
        {
            _logger.LogError(
                "Payment provider credential rotation failed Provider={Provider} TenantHash={TenantHash} Reason=encryption_unavailable",
                PaymentLogValue.Label(current.ProviderName),
                PaymentLogValue.Hash(tenantId));

            return PaymentProviderMutationResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_provider_rotation_unavailable",
                "The provider credentials could not be rotated.",
                correlationId);
        }

        Payment.DomainService.Entities.PaymentProvider? updated;

        try
        {
            updated =
                await _repository.TryRotateProviderCredentialsAsync(
                    tenantId,
                    paymentProviderId,
                    request.Version!.Value,
                    providerCiphertext,
                    tenantCiphertext,
                    providerKeyId,
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
            return PaymentProviderMutationResult.Failure(
                PaymentFailureKind.Conflict,
                "payment_provider_version_conflict",
                "The payment provider was changed by another request. Reload it and retry.",
                correlationId);
        }

        await RefreshCacheAsync(
            tenantId,
            updated.OrganizationId,
            updated.ProviderName,
            updated.IsEnabled,
            cancellationToken);

        _logger.LogInformation(
            "Payment provider credentials rotated Provider={Provider} TenantHash={TenantHash} Version={Version} ApiKeyRotated={ApiKeyRotated} WebhookKeyRotated={WebhookKeyRotated} TokenKeyRotated={TokenKeyRotated}",
            PaymentLogValue.Label(updated.ProviderName),
            PaymentLogValue.Hash(tenantId),
            updated.Version,
            request.ApiKey != null,
            request.WebhookHmacKey != null,
            request.TokenHmacKey != null);

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
            // cached under each of their keys — already decrypted — and evicting one would
            // leave the rest holding the credentials that were just rotated away.
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
                "Payment provider cache refresh failed after credential rotation Provider={Provider} TenantHash={TenantHash}",
                PaymentLogValue.Label(providerName),
                PaymentLogValue.Hash(tenantId));

            return;
        }

        if (refreshed == null && expectAvailable)
        {
            _logger.LogError(
                "Payment provider cache refresh failed after credential rotation Provider={Provider} TenantHash={TenantHash}",
                PaymentLogValue.Label(providerName),
                PaymentLogValue.Hash(tenantId));
        }
    }

    private static PaymentProviderMutationResult ValidationFailure(
        string code,
        string message,
        string correlationId) =>
        PaymentProviderMutationResult.Failure(
            PaymentFailureKind.Validation,
            code,
            message,
            correlationId);

    private PaymentProviderMutationResult StoreUnavailable(
        Exception exception,
        string tenantId,
        string correlationId)
    {
        _logger.LogError(
            exception,
            "Payment provider credential persistence failed TenantHash={TenantHash}",
            PaymentLogValue.Hash(tenantId));

        return PaymentProviderMutationResult.Failure(
            PaymentFailureKind.Unavailable,
            "payment_provider_store_unavailable",
            "The provider credentials could not be rotated.",
            correlationId);
    }
}
