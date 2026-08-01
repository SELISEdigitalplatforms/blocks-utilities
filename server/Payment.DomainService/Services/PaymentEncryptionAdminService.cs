using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentEncryptionAdminService :
    IPaymentEncryptionAdminService
{
    private readonly IPaymentExecutionContextResolver _contextResolver;
    private readonly IProviderTokenEncryptionKeyRingProvider _keyRings;
    private readonly IPaymentSecretReEncryptionService _reEncryption;
    private readonly ILogger<PaymentEncryptionAdminService> _logger;

    public PaymentEncryptionAdminService(
        IPaymentExecutionContextResolver contextResolver,
        IProviderTokenEncryptionKeyRingProvider keyRings,
        IPaymentSecretReEncryptionService reEncryption,
        ILogger<PaymentEncryptionAdminService> logger)
    {
        _contextResolver = contextResolver;
        _keyRings = keyRings;
        _reEncryption = reEncryption;
        _logger = logger;
    }

    public async Task<PaymentEncryptionHealthResult> GetHealthAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = _contextResolver.Resolve(correlationId);

        if (!resolution.IsSuccess)
        {
            var failure = resolution.Failure!;

            return new PaymentEncryptionHealthResult(
                false,
                null,
                failure.FailureKind,
                failure.ErrorCode,
                failure.ErrorMessage,
                correlationId);
        }

        var health = await _keyRings.CheckAsync(
            Scope(resolution),
            cancellationToken);

        return new PaymentEncryptionHealthResult(
            true,
            new PaymentEncryptionHealthResponse(
                health.SecretName,
                health.IsReadable,
                health.UsedSharedKeyRing,
                health.ActiveKeyId,
                health.FailureReason),
            PaymentFailureKind.None,
            string.Empty,
            string.Empty,
            correlationId);
    }

    public async Task<PaymentEncryptionReEncryptionResult> ReEncryptAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = _contextResolver.Resolve(correlationId);

        if (!resolution.IsSuccess)
        {
            var failure = resolution.Failure!;

            return new PaymentEncryptionReEncryptionResult(
                false,
                null,
                failure.FailureKind,
                failure.ErrorCode,
                failure.ErrorMessage,
                correlationId);
        }

        var scope = Scope(resolution);
        var health = await _keyRings.CheckAsync(scope, cancellationToken);

        if (!health.IsReadable)
        {
            return Unavailable(
                correlationId,
                "payment_encryption_key_ring_unavailable",
                "The encryption key ring for this organization could not be read.");
        }

        if (health.UsedSharedKeyRing)
        {
            // Re-encrypting onto the shared ring would move nothing and report success,
            // which is worse than refusing: it looks like the migration ran.
            return Unavailable(
                correlationId,
                "payment_encryption_key_ring_not_provisioned",
                "This organization has no key ring of its own yet; provision it before re-encrypting.");
        }

        _logger.LogInformation(
            "Payment secret re-encryption requested Scope={Scope} CorrelationId={CorrelationId}",
            scope.ToString(),
            correlationId);

        var summary = await _reEncryption.ReEncryptAsync(
            scope,
            cancellationToken);

        return new PaymentEncryptionReEncryptionResult(
            true,
            new PaymentEncryptionReEncryptionResponse(
                summary.ProvidersReEncrypted,
                summary.StoredPaymentMethodsReEncrypted,
                summary.Skipped,
                summary.Failed),
            PaymentFailureKind.None,
            string.Empty,
            string.Empty,
            correlationId);
    }

    /// <summary>
    /// Always the caller's own tenant and organization, never a parameter — otherwise this
    /// would rewrite another tenant's ciphertext under the caller's key.
    /// </summary>
    private static PaymentEncryptionScope Scope(
        PaymentContextResolution resolution) =>
        new(
            resolution.Context!.TenantId,
            resolution.Context.OrganizationId);

    private static PaymentEncryptionReEncryptionResult Unavailable(
        string correlationId,
        string errorCode,
        string errorMessage) =>
        new(
            false,
            null,
            PaymentFailureKind.Unavailable,
            errorCode,
            errorMessage,
            correlationId);
}
