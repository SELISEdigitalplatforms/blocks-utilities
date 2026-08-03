using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Re-encrypts one scope's provider credentials and saved card tokens under the active key of
/// that scope's key ring.
/// </summary>
/// <remarks>
/// Idempotent and resumable. Every write is a compare-and-set on the key id the ciphertext was
/// read under, so a record changed by live traffic mid-run is skipped rather than overwritten,
/// and a second run over an unchanged scope writes nothing.
/// <para>
/// Both key versions must be present in the ring while this runs: it decrypts under the old key
/// and encrypts under the new one. Removing the old key before the job completes strands every
/// record still on it.
/// </para>
/// </remarks>
public sealed class PaymentSecretReEncryptionService :
    IPaymentSecretReEncryptionService
{
    private const int PageSize = 200;

    private readonly IPaymentRepository _payments;
    private readonly IStoredPaymentMethodRepository _methods;
    private readonly IProviderTokenEncryptionKeyRingProvider _keyRings;
    private readonly IAesGcmSecretProtector _protector;
    private readonly IProviderTokenProtector _tokenProtector;
    private readonly ILogger<PaymentSecretReEncryptionService> _logger;

    public PaymentSecretReEncryptionService(
        IPaymentRepository payments,
        IStoredPaymentMethodRepository methods,
        IProviderTokenEncryptionKeyRingProvider keyRings,
        IAesGcmSecretProtector protector,
        IProviderTokenProtector tokenProtector,
        ILogger<PaymentSecretReEncryptionService> logger)
    {
        _payments = payments;
        _methods = methods;
        _keyRings = keyRings;
        _protector = protector;
        _tokenProtector = tokenProtector;
        _logger = logger;
    }

    public async Task<PaymentSecretReEncryptionSummary> ReEncryptAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken)
    {
        var keyRing = await _keyRings.GetAsync(scope, cancellationToken);
        var activeKeyId = keyRing.ActiveKeyId;

        if (string.IsNullOrWhiteSpace(activeKeyId))
        {
            _logger.LogError(
                "Payment secret re-encryption cannot run for {Scope}: its key ring is unavailable.",
                scope.ToString());

            return new PaymentSecretReEncryptionSummary(0, 0, 0, 0);
        }

        var providers = await ReEncryptProvidersAsync(
            scope,
            activeKeyId,
            cancellationToken);
        var methods = await ReEncryptStoredMethodsAsync(
            scope,
            activeKeyId,
            cancellationToken);

        var summary = new PaymentSecretReEncryptionSummary(
            providers.ReEncrypted,
            methods.ReEncrypted,
            providers.Skipped + methods.Skipped,
            providers.Failed + methods.Failed);

        _logger.LogInformation(
            "Payment secret re-encryption completed Scope={Scope} ActiveKeyId={ActiveKeyId} Providers={Providers} StoredMethods={StoredMethods} Skipped={Skipped} Failed={Failed}",
            scope.ToString(),
            activeKeyId,
            summary.ProvidersReEncrypted,
            summary.StoredPaymentMethodsReEncrypted,
            summary.Skipped,
            summary.Failed);

        return summary;
    }

    private async Task<Counters> ReEncryptProvidersAsync(
        PaymentEncryptionScope scope,
        string activeKeyId,
        CancellationToken cancellationToken)
    {
        var counters = new Counters();
        var providers = await _payments.GetProvidersAsync(
            scope.TenantId,
            cancellationToken);

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A tenant's providers span its organizations; each is re-encrypted under its own
            // ring, in its own run.
            if (!string.Equals(
                    provider.OrganizationId,
                    scope.OrganizationId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var keyId = provider.SecretsEncryptionKeyId;

            if (string.IsNullOrWhiteSpace(keyId) ||
                string.IsNullOrWhiteSpace(provider.ProviderSecretsCiphertext) ||
                string.IsNullOrWhiteSpace(provider.TenantSecuritySecretsCiphertext) ||
                string.Equals(keyId, activeKeyId, StringComparison.Ordinal))
            {
                counters.Skipped++;

                continue;
            }

            if (!await ReEncryptProviderAsync(
                    scope,
                    provider,
                    keyId,
                    cancellationToken,
                    counters))
            {
                continue;
            }

            counters.ReEncrypted++;
        }

        return counters;
    }

    private async Task<bool> ReEncryptProviderAsync(
        PaymentEncryptionScope scope,
        PaymentProvider provider,
        string keyId,
        CancellationToken cancellationToken,
        Counters counters)
    {
        var credentials = await _protector.UnprotectAsync(
            scope,
            provider.ProviderSecretsCiphertext!,
            keyId,
            cancellationToken);
        var tenantSecurity = await _protector.UnprotectAsync(
            scope,
            provider.TenantSecuritySecretsCiphertext!,
            keyId,
            cancellationToken);

        if (!credentials.IsRead || !tenantSecurity.IsRead)
        {
            _logger.LogError(
                "Payment provider secrets could not be re-encrypted Provider={Provider} TenantHash={TenantHash} Reason=secrets_unreadable",
                PaymentLogValue.Label(provider.ProviderName),
                PaymentLogValue.Hash(provider.TenantId));
            counters.Failed++;

            return false;
        }

        var reCredentials = await _protector.ProtectAsync(
            scope,
            credentials.Plaintext,
            cancellationToken);
        var reTenantSecurity = await _protector.ProtectAsync(
            scope,
            tenantSecurity.Plaintext,
            cancellationToken);

        if (!reCredentials.IsProtected || !reTenantSecurity.IsProtected)
        {
            counters.Failed++;

            return false;
        }

        var replaced = await _payments.ReplaceProviderSecretsAsync(
            provider.TenantId,
            provider.ItemId,
            keyId,
            reCredentials.Ciphertext,
            reTenantSecurity.Ciphertext,
            reCredentials.KeyId,
            cancellationToken);

        if (!replaced)
        {
            // Rotated by something else between the read and the write.
            counters.Skipped++;

            return false;
        }

        return true;
    }

    private async Task<Counters> ReEncryptStoredMethodsAsync(
        PaymentEncryptionScope scope,
        string activeKeyId,
        CancellationToken cancellationToken)
    {
        var counters = new Counters();
        string? afterItemId = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await _methods.ListForReEncryptionAsync(
                scope.TenantId,
                scope.OrganizationId,
                activeKeyId,
                afterItemId,
                PageSize,
                cancellationToken);

            if (page.Count == 0) break;

            foreach (var method in page)
            {
                await ReEncryptStoredMethodAsync(
                    scope,
                    method,
                    cancellationToken,
                    counters);
            }

            afterItemId = page[^1].ItemId;
        }

        return counters;
    }

    private async Task ReEncryptStoredMethodAsync(
        PaymentEncryptionScope scope,
        StoredPaymentMethod method,
        CancellationToken cancellationToken,
        Counters counters)
    {
        var keyId = method.TokenEncryptionKeyId;

        if (string.IsNullOrWhiteSpace(keyId))
        {
            counters.Skipped++;

            return;
        }

        var token = await _tokenProtector.UnprotectAsync(
            method,
            cancellationToken);

        if (!token.IsRead)
        {
            _logger.LogError(
                "A stored payment method token could not be re-encrypted TenantHash={TenantHash} PaymentMethodHash={PaymentMethodHash} Reason=token_unreadable",
                PaymentLogValue.Hash(method.TenantId),
                PaymentLogValue.Hash(method.ItemId));
            counters.Failed++;

            return;
        }

        var protection = await _tokenProtector.ProtectAsync(
            scope,
            token.ProviderToken,
            cancellationToken);

        if (!protection.IsProtected)
        {
            counters.Failed++;

            return;
        }

        var replaced = await _methods.ReplaceProtectedTokenAsync(
            method.TenantId,
            method.ItemId,
            keyId,
            protection.Token!,
            cancellationToken);

        if (replaced)
        {
            counters.ReEncrypted++;
        }
        else
        {
            // Re-saved, removed or already migrated between the read and the write.
            counters.Skipped++;
        }
    }

    private sealed class Counters
    {
        public int ReEncrypted { get; set; }

        public int Skipped { get; set; }

        public int Failed { get; set; }
    }
}
