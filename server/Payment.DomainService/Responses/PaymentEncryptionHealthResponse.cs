namespace Payment.DomainService.Responses;

/// <param name="SecretName">
/// The vault secret the scope expects, so an operator can go and look. Computed from the tenant
/// and organization, so this is also what to create when the ring is missing.
/// </param>
/// <param name="IsReadable">False when the secret is missing, malformed, or unreachable.</param>
/// <param name="UsesSharedKeyRing">
/// True when the scope has no ring of its own and is running on the pre-migration shared ring.
/// It works, but it is not isolated — this is the state the migration exists to clear.
/// </param>
/// <param name="ActiveKeyId">
/// Which key new writes use. Never the key itself, only its id.
/// </param>
public sealed record PaymentEncryptionHealthResponse(
    string SecretName,
    bool IsReadable,
    bool UsesSharedKeyRing,
    string ActiveKeyId,
    string FailureReason);

/// <param name="Skipped">Records already on the active key, or changed by live traffic mid-run.</param>
/// <param name="Failed">Records that could not be decrypted at all; these need an operator.</param>
public sealed record PaymentEncryptionReEncryptionResponse(
    int ProvidersReEncrypted,
    int StoredPaymentMethodsReEncrypted,
    int Skipped,
    int Failed);
