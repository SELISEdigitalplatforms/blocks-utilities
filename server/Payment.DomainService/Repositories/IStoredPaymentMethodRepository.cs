using Payment.DomainService.Entities;
using Payment.DomainService.Services;

namespace Payment.DomainService.Repositories;

public interface IStoredPaymentMethodRepository
{
    /// <summary>
    /// Active methods for any of the supplied shopper references.
    /// </summary>
    /// <remarks>
    /// Takes a set rather than one reference because the reference is derived per provider,
    /// from that provider's own key. A shopper with cards at two providers therefore has two
    /// references, and listing by a single one would silently hide half their cards.
    /// </remarks>
    Task<List<StoredPaymentMethod>> ListActiveAsync(
        string tenantId,
        IReadOnlyCollection<string> shopperReferences,
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

    /// <summary>
    /// The active method already holding this card, whatever token it was saved under.
    /// </summary>
    Task<StoredPaymentMethod?> GetByCardFingerprintAsync(
        string tenantId,
        string? organizationId,
        string shopperReference,
        string providerName,
        string cardFingerprint,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves an existing card record onto a newly issued token, so re-saving a card the
    /// shopper already has updates that record rather than adding a second one.
    /// </summary>
    Task<bool> SupersedeTokenAsync(
        StoredPaymentMethod method,
        DateTime eventDateUtc,
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

    Task<StoredPaymentMethod?> TryClaimForPaymentAsync(
        string tenantId,
        string itemId,
        string shopperReference,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task ReleasePaymentClaimAsync(
        string tenantId,
        string itemId,
        string leaseId,
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
