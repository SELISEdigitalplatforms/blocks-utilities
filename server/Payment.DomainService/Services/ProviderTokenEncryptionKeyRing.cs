using System.Security.Cryptography;

namespace Payment.DomainService.Services;

public sealed class ProviderTokenEncryptionKeyRing :
    IProviderTokenEncryptionKeyRing,
    IDisposable
{
    private readonly Dictionary<string, byte[]> _keys;
    private bool _disposed;

    public ProviderTokenEncryptionKeyRing(
        string activeKeyId,
        IReadOnlyDictionary<string, byte[]> keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeKeyId);
        ArgumentNullException.ThrowIfNull(keys);

        if (!keys.ContainsKey(activeKeyId))
        {
            throw new ArgumentException(
                "The active provider-token encryption key is missing.",
                nameof(keys));
        }

        if (keys.Any(
                pair =>
                    string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value == null ||
                    pair.Value.Length is not (16 or 24 or 32)))
        {
            throw new ArgumentException(
                "A provider-token encryption key is invalid.",
                nameof(keys));
        }

        ActiveKeyId = activeKeyId;
        _keys = keys.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    public string ActiveKeyId { get; }

    public bool TryGetKey(
        string keyId,
        out byte[] key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_keys.TryGetValue(keyId, out var storedKey))
        {
            key = storedKey.ToArray();

            return true;
        }

        key = [];

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var key in _keys.Values)
        {
            CryptographicOperations.ZeroMemory(key);
        }

        _keys.Clear();
        _disposed = true;
    }
}
