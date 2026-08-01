using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public interface IPaymentRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);
    /// <summary>
    /// The configuration this organization pays through, falling back to the tenant's own.
    /// </summary>
    /// <remarks>
    /// Organizations within a tenant may be separate businesses with their own merchant
    /// accounts, so each may hold its own configuration. A tenant-level configuration — one
    /// with no organization — serves any organization that has not registered its own.
    /// </remarks>
    Task<PaymentProvider?> GetProviderAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a provider configuration. Returns <see langword="false"/> when one already
    /// exists for the same tenant, provider and merchant, which the unique index decides.
    /// </summary>
    Task<bool> TryCreateProviderAsync(
        PaymentProvider provider,
        CancellationToken cancellationToken);

    /// <summary>Every provider configured for a tenant, enabled or not.</summary>
    Task<IReadOnlyList<PaymentProvider>> GetProvidersAsync(
        string tenantId,
        CancellationToken cancellationToken);

    Task<PaymentProvider?> GetProviderByIdAsync(
        string tenantId,
        string providerItemId,
        CancellationToken cancellationToken);

    Task<PaymentProvider?> TryUpdateProviderConfigurationAsync(
        string tenantId,
        string providerItemId,
        long expectedVersion,
        string frontendResultUrl,
        string? countryCode,
        bool manualCapture,
        int maxRefundDays,
        string? storeId,
        bool isEnabled,
        CancellationToken cancellationToken);

    Task<PaymentProvider?> TryRotateProviderCredentialsAsync(
        string tenantId,
        string providerItemId,
        long expectedVersion,
        string providerSecretsCiphertext,
        string tenantSecuritySecretsCiphertext,
        string encryptionKeyId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the encrypted credential blobs onto a provider. Only applies when the provider
    /// has none yet, so re-running a migration cannot overwrite live credentials.
    /// </summary>
    Task<bool> SaveProviderSecretsAsync(
        string tenantId,
        string providerItemId,
        string providerSecretsCiphertext,
        string tenantSecuritySecretsCiphertext,
        string encryptionKeyId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a provider's credential blobs onto a new encryption key.
    /// </summary>
    /// <remarks>
    /// Compare-and-set on <paramref name="expectedKeyId"/> rather than on absence, because this
    /// rewrites live credentials rather than filling in missing ones. A provider whose key has
    /// already changed — by a concurrent rotation, or by an earlier run of the same job — is
    /// left alone and reported as unchanged, which is what makes a repeated run a no-op.
    /// </remarks>
    Task<bool> ReplaceProviderSecretsAsync(
        string tenantId,
        string providerItemId,
        string expectedKeyId,
        string providerSecretsCiphertext,
        string tenantSecuritySecretsCiphertext,
        string encryptionKeyId,
        CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(PaymentDetail payment, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetByIdAsync(string tenantId, string paymentId, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetByPspReferenceAsync(string tenantId, string pspReference, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetRecurringPaymentByOrderIdAsync(
        string tenantId,
        string orderId,
        CancellationToken cancellationToken);
    Task<PaymentDetail?> TryClaimInitiationAsync(string tenantId, string paymentId, string leaseId, DateTime leaseUntilUtc, CancellationToken cancellationToken);
    Task<bool> SaveInitiationRequestAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        Payment.DomainService.Models.ProviderInitiationRequest request,
        string frontendResultUrlSnapshot,
        string returnStateNonceHash,
        string shopperReference,
        CancellationToken cancellationToken);
    Task<bool> CompleteInitiationAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string status,
        string? sessionId,
        string? sessionData,
        string? redirectUrl,
        DateTime? expiresAtUtc,
        string? failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
    Task MarkInitiationUnknownAsync(string tenantId, string paymentId, string leaseId, string failureCode, CancellationToken cancellationToken);
    Task<bool> CompleteStoredPaymentChargeInitiationAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string pspReference,
        string? providerResultCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
    Task<bool> SaveProviderRoutingAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string providerReference,
        string merchantAccount,
        CancellationToken cancellationToken);
    Task<bool> SaveCheckoutObservationAsync(
        string tenantId,
        string paymentId,
        string sessionStatus,
        string? resultCode,
        string sessionResultHash,
        string? pspReference,
        PaymentInstrument? instrument,
        CancellationToken cancellationToken);
    Task<bool> ApplyAuthorisationAsync(
        string tenantId,
        string paymentId,
        bool authorized,
        decimal authorizedAmount,
        bool capturedAutomatically,
        string pspReference,
        DateTime eventDateUtc,
        PaymentInstrument? instrument,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
    Task<List<PaymentDetail>> GetPaymentsWithDueOutboxEventsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task<bool> TryClaimOutboxEventAsync(string tenantId, string paymentId, string eventId, string leaseId, DateTime leaseUntilUtc, CancellationToken cancellationToken);
    Task MarkOutboxPublishedAsync(string tenantId, string paymentId, string eventId, string leaseId, DateTime utcNow, CancellationToken cancellationToken);
    Task MarkOutboxFailedAsync(
        string tenantId,
        string paymentId,
        string eventId,
        string leaseId,
        PaymentOutboxStatus status,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string error,
        CancellationToken cancellationToken);
    Task<List<PaymentDetail>> GetStaleInitiationsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task<bool> HasUnresolvedRecurringPaymentAsync(
        string tenantId,
        string storedPaymentMethodId,
        CancellationToken cancellationToken);
}
