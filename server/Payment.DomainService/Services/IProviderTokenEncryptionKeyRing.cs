namespace Payment.DomainService.Services;

public interface IProviderTokenEncryptionKeyRing : IDisposable
{
    string ActiveKeyId { get; }

    bool TryGetKey(
        string keyId,
        out byte[] key);
}
