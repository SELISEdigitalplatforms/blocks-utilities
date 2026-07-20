namespace Payment.DomainService.Utilities;

public sealed class ProviderTokenEncryptionKeyRingSecret
{
    public string ActiveKeyId { get; set; } = string.Empty;

    public Dictionary<string, string> Keys { get; set; } =
        new(StringComparer.Ordinal);
}
