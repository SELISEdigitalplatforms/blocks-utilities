namespace Payment.DomainService.Services;

public interface IProviderSecretMigrationService
{
    /// <summary>
    /// Moves any vault-backed provider credentials for a tenant onto their documents,
    /// encrypted. Idempotent: already-migrated providers are left alone.
    /// </summary>
    Task<ProviderSecretMigrationSummary> MigrateAsync(
        string tenantId,
        CancellationToken cancellationToken);
}

/// <param name="Migrated">Providers moved onto encrypted storage by this run.</param>
/// <param name="Skipped">Providers that already held encrypted credentials.</param>
/// <param name="Failed">Providers that could not be migrated and remain unusable.</param>
public sealed record ProviderSecretMigrationSummary(
    int Migrated,
    int Skipped,
    int Failed);
