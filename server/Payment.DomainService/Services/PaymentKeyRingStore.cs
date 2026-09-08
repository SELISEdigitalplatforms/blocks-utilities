using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Creates a scope's key ring when it has none. Never modifies one that exists.
/// </summary>
public sealed class PaymentKeyRingStore : IPaymentKeyRingStore
{
    private const int MaximumKeyCount = 16;
    private const int KeyLengthBytes = 32;

    private static readonly JsonSerializerOptions ReadOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IKeyVaultSecretGateway _gateway;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentKeyRingStore> _logger;
    private readonly TimeProvider _time;

    public PaymentKeyRingStore(
        IKeyVaultSecretGateway gateway,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentKeyRingStore> logger,
        TimeProvider? time = null)
    {
        _gateway = gateway;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<KeyRingProvisionOutcome> TryCreateAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken)
    {
        var secretName = PaymentKeyRingSecretName.Create(scope);

        if (secretName.Length > 127)
        {
            _logger.LogError(
                "Payment key ring provisioning is unavailable Reason=secret_name_too_long");

            return KeyRingProvisionOutcome.Unavailable;
        }

        var existing = await _gateway.TryReadAsync(
            secretName,
            cancellationToken);

        switch (existing.Outcome)
        {
            // The whole point of this class. A ring that exists is never touched, because
            // replacing its active key makes everything encrypted under the old one
            // unreadable.
            case KeyVaultReadOutcome.Found:
                return KeyRingProvisionOutcome.AlreadyExists;

            case KeyVaultReadOutcome.Unavailable:
                return KeyRingProvisionOutcome.Unavailable;
        }

        var keys = await SeedFromSharedRingAsync(cancellationToken);
        var activeKeyId = NextKeyId(keys, _time.GetUtcNow().UtcDateTime);

        keys[activeKeyId] = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(KeyLengthBytes));

        if (keys.Count > MaximumKeyCount)
        {
            _logger.LogError(
                "Payment key ring provisioning is unavailable Reason=too_many_seeded_keys Count={Count}",
                keys.Count);

            return KeyRingProvisionOutcome.Unavailable;
        }

        var payload = JsonSerializer.Serialize(
            new ProviderTokenEncryptionKeyRingSecret
            {
                ActiveKeyId = activeKeyId,
                Keys = keys
            },
            WriteOptions);

        if (!await _gateway.TrySetAsync(
                secretName,
                payload,
                cancellationToken))
        {
            return KeyRingProvisionOutcome.Unavailable;
        }

        _logger.LogInformation(
            "Payment key ring provisioned SecretName={SecretName} ActiveKeyId={ActiveKeyId} SeededKeys={SeededKeys}",
            secretName,
            activeKeyId,
            keys.Count - 1);

        return KeyRingProvisionOutcome.Created;
    }

    /// <summary>
    /// Copies the shared ring's keys in as non-active entries.
    /// </summary>
    /// <remarks>
    /// While <see cref="PaymentOptions.FallBackToSharedEncryptionKeyRing"/> is on, a scope
    /// with no ring of its own writes under the shared one. Giving it a ring stops that
    /// fallback, so without this step every record it had already written would become
    /// unreadable the moment it was provisioned. Same reasoning as the script's
    /// <c>-ImportSharedKey</c>: old keys present but not active, new writes on the new key,
    /// re-encryption and removal left to the operator.
    /// </remarks>
    private async Task<Dictionary<string, string>> SeedFromSharedRingAsync(
        CancellationToken cancellationToken)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!_options.CurrentValue.FallBackToSharedEncryptionKeyRing)
        {
            return keys;
        }

        var shared = await _gateway.TryReadAsync(
            PaymentKeyRingSecretName.SharedSecretName,
            cancellationToken);

        if (shared.Outcome != KeyVaultReadOutcome.Found ||
            string.IsNullOrWhiteSpace(shared.Value))
        {
            return keys;
        }

        try
        {
            var secret =
                JsonSerializer.Deserialize<ProviderTokenEncryptionKeyRingSecret>(
                    shared.Value,
                    ReadOptions);

            foreach (var pair in secret?.Keys ??
                     new Dictionary<string, string>(StringComparer.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) &&
                    !string.IsNullOrWhiteSpace(pair.Value))
                {
                    keys[pair.Key] = pair.Value;
                }
            }
        }
        catch (JsonException exception)
        {
            // A malformed shared ring is not a reason to refuse a new scope its own key. It
            // does mean anything already written under the shared ring stays unreadable, so
            // it is worth an error rather than silence.
            _logger.LogError(
                exception,
                "The shared payment key ring could not be read while seeding a new one");
        }

        return keys;
    }

    private static string NextKeyId(Dictionary<string, string> keys, DateTime nowUtc)
    {
        var candidate =
            $"payment-token-key-{nowUtc:yyyy-MM}";

        return keys.ContainsKey(candidate)
            ? $"payment-token-key-{nowUtc:yyyy-MM-dd-HHmmss}"
            : candidate;
    }
}
