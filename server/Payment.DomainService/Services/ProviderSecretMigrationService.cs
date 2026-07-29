using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Moves provider credentials from Key Vault onto the provider document, encrypted.
/// </summary>
/// <remarks>
/// The vault JSON is encrypted <em>verbatim</em> rather than being parsed and rebuilt. That is
/// deliberate: <c>shopperReferenceHmacKey</c> is an identity key from which shopper references
/// are derived, and stored payment methods are keyed by those references. Any transformation
/// that altered it — even reformatting — would orphan every stored card with no error anywhere,
/// so the bytes are carried across untouched.
///
/// Safe to run repeatedly: providers that already hold encrypted credentials are skipped, and
/// the write itself only applies when the document has none.
/// </remarks>
public sealed class ProviderSecretMigrationService : IProviderSecretMigrationService
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentTenantContextScopeFactory _tenantScopes;
    private readonly IAesGcmSecretProtector _protector;
    private readonly IVault _vault;
    private readonly ILogger<ProviderSecretMigrationService> _logger;

    public ProviderSecretMigrationService(
        IPaymentRepository repository,
        IPaymentTenantContextScopeFactory tenantScopes,
        IAesGcmSecretProtector protector,
        IVault vault,
        ILogger<ProviderSecretMigrationService> logger)
    {
        _repository = repository;
        _tenantScopes = tenantScopes;
        _protector = protector;
        _vault = vault;
        _logger = logger;
    }

    public async Task<ProviderSecretMigrationSummary> MigrateAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        using var scope = _tenantScopes.Establish(tenantId);

        var providers = await _repository.GetProvidersAsync(tenantId, cancellationToken);
        var migrated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!NeedsMigration(provider))
            {
                skipped++;

                continue;
            }

            if (await MigrateProviderAsync(tenantId, provider, cancellationToken))
            {
                migrated++;
            }
            else
            {
                failed++;
            }
        }

        _logger.LogInformation(
            "Provider secret migration completed TenantHash={TenantHash} Migrated={Migrated} Skipped={Skipped} Failed={Failed}",
            PaymentLogValue.Hash(tenantId),
            migrated,
            skipped,
            failed);

        return new ProviderSecretMigrationSummary(migrated, skipped, failed);
    }

    private static bool NeedsMigration(PaymentProvider provider) =>
        string.IsNullOrWhiteSpace(provider.ProviderSecretsCiphertext) &&
        !string.IsNullOrWhiteSpace(provider.ProviderCredentialSecretName) &&
        !string.IsNullOrWhiteSpace(provider.TenantSecuritySecretName);

    private async Task<bool> MigrateProviderAsync(
        string tenantId,
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        var credentialSecretName = provider.ProviderCredentialSecretName!;
        var tenantSecretName = provider.TenantSecuritySecretName!;

        try
        {
            var secrets = await _vault.ProcessSecretsAsync(
                [credentialSecretName, tenantSecretName]);

            if (!secrets.TryGetValue(credentialSecretName, out var credentialJson) ||
                !secrets.TryGetValue(tenantSecretName, out var tenantJson) ||
                string.IsNullOrWhiteSpace(credentialJson) ||
                string.IsNullOrWhiteSpace(tenantJson))
            {
                return LogFailure(provider, "vault_secret_missing");
            }

            if (!_protector.TryProtect(credentialJson, out var credentialCiphertext, out var keyId) ||
                !_protector.TryProtect(tenantJson, out var tenantCiphertext, out _))
            {
                return LogFailure(provider, "encryption_unavailable");
            }

            var saved = await _repository.SaveProviderSecretsAsync(
                tenantId,
                provider.ItemId,
                credentialCiphertext,
                tenantCiphertext,
                keyId,
                cancellationToken);

            if (!saved)
            {
                return LogFailure(provider, "already_migrated_or_changed");
            }

            _logger.LogInformation(
                "Provider secrets migrated Provider={Provider} TenantHash={TenantHash}",
                PaymentLogValue.Label(provider.ProviderName),
                PaymentLogValue.Hash(tenantId));

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Provider secret migration failed Provider={Provider} TenantHash={TenantHash} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                PaymentLogValue.Hash(tenantId),
                exception.GetType().Name);

            return false;
        }
    }

    private bool LogFailure(PaymentProvider provider, string reason)
    {
        _logger.LogError(
            "Provider secrets could not be migrated Provider={Provider} TenantHash={TenantHash} Reason={Reason}",
            PaymentLogValue.Label(provider.ProviderName),
            PaymentLogValue.Hash(provider.TenantId),
            reason);

        return false;
    }
}
