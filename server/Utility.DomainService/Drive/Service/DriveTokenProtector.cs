using System.Security.Cryptography;
using System.Text;

namespace Utility.DomainService.Drive.Service;

/// <summary>
/// AES-GCM for refresh tokens at rest, keyed from the Blocks secret
/// <see cref="DriveOptions.TokenEncryptionKeySecretId"/> names.
/// </summary>
/// <remarks>
/// Same payload layout as the payment protector (nonce, then tag, then ciphertext, one base64
/// string) so a reader who knows one knows the other, but deliberately not that class: it is bound
/// to a per-organization Key Vault key ring, and a drive connection has one key for the whole
/// deployment. GCM is authenticated, so a row altered in storage fails to decrypt rather than
/// yielding garbage that gets sent to Entra.
/// </remarks>
public sealed class DriveTokenProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IDriveSecrets _secrets;

    public DriveTokenProtector(IDriveSecrets secrets) => _secrets = secrets;

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) =>
        TryParseKey(await _secrets.GetTokenEncryptionKeyAsync(cancellationToken), out _);

    public async Task<string?> ProtectAsync(string plaintext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(plaintext) || !TryParseKey(await _secrets.GetTokenEncryptionKeyAsync(cancellationToken), out var key))
        {
            return null;
        }

        try
        {
            var plain = Encoding.UTF8.GetBytes(plaintext);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plain, cipher, tag);
            }

            var payload = new byte[NonceSize + TagSize + cipher.Length];
            nonce.CopyTo(payload, 0);
            tag.CopyTo(payload, NonceSize);
            cipher.CopyTo(payload, NonceSize + TagSize);
            return Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<string?> UnprotectAsync(string ciphertext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ciphertext) || !TryParseKey(await _secrets.GetTokenEncryptionKeyAsync(cancellationToken), out var key))
        {
            return null;
        }

        try
        {
            var payload = Convert.FromBase64String(ciphertext);
            if (payload.Length < NonceSize + TagSize)
            {
                return null;
            }

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var cipher = payload.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static bool TryParseKey(string? encoded, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        return key.Length is 16 or 24 or 32;
    }
}
