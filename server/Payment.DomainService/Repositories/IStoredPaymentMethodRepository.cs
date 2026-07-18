using Payment.DomainService.Entities;
using Payment.DomainService.Services;

namespace Payment.DomainService.Repositories;

public interface IStoredPaymentMethodRepository
{
    Task<List<StoredPaymentMethod>> ListActiveAsync(
        string tenantId,
        string shopperReference,
        CancellationToken cancellationToken);

    Task<StoredPaymentMethod?> GetAsync(
        string tenantId,
        string itemId,
        CancellationToken cancellationToken);

    Task<StoredPaymentMethod?> GetByTokenFingerprintAsync(
        string tenantId,
        string shopperReference,
        string providerName,
        string tokenFingerprint,
        CancellationToken cancellationToken);

    Task<bool> HasUnresolvedRemovalAsync(
        string tenantId,
        string shopperReference,
        CancellationToken cancellationToken);

    Task UpsertFromProviderAsync(
        StoredPaymentMethod method,
        DateTime eventDateUtc,
        CancellationToken cancellationToken);

    Task<bool> ReactivateAfterFreshConsentAsync(
        StoredPaymentMethod method,
        DateTime paymentCreatedAtUtc,
        DateTime eventDateUtc,
        CancellationToken cancellationToken);

    Task<StoredPaymentMethod?> TryClaimRemovalAsync(
        string tenantId,
        string itemId,
        string shopperReference,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task<List<StoredPaymentMethod>> GetDueRemovalCandidatesAsync(
        string tenantId,
        DateTime utcNow,
        int limit,
        CancellationToken cancellationToken);

    Task<StoredPaymentMethod?> TryClaimDueRemovalAsync(
        string tenantId,
        string itemId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<bool> MarkRemovalOutcomeUnknownAsync(
        string tenantId,
        string itemId,
        string leaseId,
        DateTime nextAttemptAtUtc,
        string errorCode,
        CancellationToken cancellationToken);

    Task<bool> MarkRemovedAsync(
        string tenantId,
        string itemId,
        string leaseId,
        DateTime removedAtUtc,
        CancellationToken cancellationToken);

    Task MarkRemovedFromProviderAsync(
        string tenantId,
        string shopperReference,
        string tokenFingerprint,
        DateTime eventDateUtc,
        CancellationToken cancellationToken);

    Task<bool> MarkRemovalRequiresAttentionAsync(
        string tenantId,
        string itemId,
        string leaseId,
        string errorCode,
        CancellationToken cancellationToken);

    Task MigrateLegacyTokenAsync(
        string tenantId,
        string itemId,
        ProtectedProviderToken protectedToken,
        CancellationToken cancellationToken);
}
