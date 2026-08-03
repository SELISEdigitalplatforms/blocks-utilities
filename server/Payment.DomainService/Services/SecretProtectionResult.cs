namespace Payment.DomainService.Services;

/// <param name="IsProtected">False when no usable key was available. Nothing may be stored.</param>
public sealed record SecretProtectionResult(
    bool IsProtected,
    string Ciphertext,
    string KeyId)
{
    public static readonly SecretProtectionResult Failed =
        new(false, string.Empty, string.Empty);
}

/// <param name="IsRead">
/// False when the key was unavailable, the payload was malformed, or authentication failed.
/// </param>
public sealed record SecretReadResult(
    bool IsRead,
    string Plaintext)
{
    public static readonly SecretReadResult Failed =
        new(false, string.Empty);
}
