namespace Payment.DomainService.Services;

public sealed class UnavailableProviderTokenEncryptionKeyRing :
    IProviderTokenEncryptionKeyRing
{
    public string ActiveKeyId => string.Empty;

    public bool TryGetKey(
        string keyId,
        out byte[] key)
    {
        key = [];

        return false;
    }

    public void Dispose()
    {
    }
}
