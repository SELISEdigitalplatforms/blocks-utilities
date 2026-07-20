using System.Text.Json;
using Blocks.Genesis;
using Payment.DomainService.Services;

namespace Payment.DomainService.Utilities;

public static class ProviderTokenEncryptionKeyRingVaultLoader
{
    public const string SecretName =
        "PaymentProviderTokenEncryptionKeyRing";

    private const int MaximumSecretCharacters = 32_768;
    private const int MaximumKeyCount = 16;
    private const int MaximumKeyIdLength = 128;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public static Task<IProviderTokenEncryptionKeyRing> LoadAsync(
        VaultType vaultType) =>
        LoadAsync(Vault.GetCloudVault(vaultType));

    public static async Task<IProviderTokenEncryptionKeyRing> LoadAsync(
        IVault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        var secrets = await vault.ProcessSecretsAsync(
            [SecretName]);

        if (!secrets.TryGetValue(SecretName, out var serializedKeyRing) ||
            string.IsNullOrWhiteSpace(serializedKeyRing))
        {
            throw InvalidSecret(
                "The provider-token encryption keyring secret is missing.");
        }

        if (serializedKeyRing.Length > MaximumSecretCharacters)
        {
            throw InvalidSecret(
                "The provider-token encryption keyring secret is too large.");
        }

        ProviderTokenEncryptionKeyRingSecret? secret;

        try
        {
            secret =
                JsonSerializer.Deserialize<
                    ProviderTokenEncryptionKeyRingSecret>(
                    serializedKeyRing,
                    SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidSecret(
                "The provider-token encryption keyring secret is malformed.",
                exception);
        }

        if (secret == null ||
            string.IsNullOrWhiteSpace(secret.ActiveKeyId) ||
            secret.ActiveKeyId.Length > MaximumKeyIdLength ||
            secret.Keys.Count is < 1 or > MaximumKeyCount)
        {
            throw InvalidSecret(
                "The provider-token encryption keyring secret is invalid.");
        }

        var decodedKeys =
            new Dictionary<string, byte[]>(
                StringComparer.Ordinal);

        try
        {
            foreach (var pair in secret.Keys)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Key.Length > MaximumKeyIdLength ||
                    !TryDecodeKey(pair.Value, out var decodedKey))
                {
                    throw InvalidSecret(
                        "The provider-token encryption keyring contains an invalid key.");
                }

                decodedKeys.Add(pair.Key, decodedKey);
            }

            if (!decodedKeys.ContainsKey(secret.ActiveKeyId))
            {
                throw InvalidSecret(
                    "The active provider-token encryption key is missing.");
            }

            return new ProviderTokenEncryptionKeyRing(
                secret.ActiveKeyId,
                decodedKeys);
        }
        finally
        {
            foreach (var key in decodedKeys.Values)
            {
                System.Security.Cryptography.CryptographicOperations
                    .ZeroMemory(key);
            }
        }
    }

    private static bool TryDecodeKey(
        string encodedKey,
        out byte[] key)
    {
        key = [];

        try
        {
            key = Convert.FromBase64String(encodedKey);

            if (key.Length is 16 or 24 or 32)
            {
                return true;
            }

            System.Security.Cryptography.CryptographicOperations
                .ZeroMemory(key);
            key = [];

            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static InvalidOperationException InvalidSecret(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}
