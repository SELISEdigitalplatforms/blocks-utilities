namespace Payment.DomainService.Utilities;

/// <summary>
/// Computes the vault secret name holding a scope's key ring.
/// </summary>
/// <remarks>
/// The name is derived from the tenant and organization, never looked up. A mapping table would
/// be a second source of truth that can drift from the vault, and drift here reads as
/// "payments unavailable" with no indication why.
/// <para>
/// <see cref="PaymentSlug"/> keeps each fragment to 49 characters and appends a hash, so two
/// identifiers cannot collide after sanitising and the whole name stays inside Key Vault's
/// 127-character limit.
/// </para>
/// </remarks>
public static class PaymentKeyRingSecretName
{
    public const string Prefix = "payment-keyring";

    /// <summary>
    /// The single ring that protected every tenant before rings were scoped. Still readable
    /// during migration, and the fallback for a scope whose own ring has not been provisioned.
    /// </summary>
    public const string SharedSecretName =
        ProviderTokenEncryptionKeyRingVaultLoader.SecretName;

    public static string Create(PaymentEncryptionScope scope)
    {
        var tenant = PaymentSlug.Create(scope.TenantId);

        return scope.OrganizationId == null
            ? $"{Prefix}-{tenant}"
            : $"{Prefix}-{tenant}-{PaymentSlug.Create(scope.OrganizationId)}";
    }
}
