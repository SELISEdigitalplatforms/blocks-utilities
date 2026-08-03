using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface IProviderTokenProtector
{
    Task<ProviderTokenProtectionResult> ProtectAsync(
        PaymentEncryptionScope scope,
        string providerToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts a saved card's token. The scope comes from the record rather than the caller,
    /// so webhook intake and background work — which have no request context — resolve the same
    /// key ring the token was written under.
    /// </summary>
    Task<ProviderTokenReadResult> UnprotectAsync(
        StoredPaymentMethod method,
        CancellationToken cancellationToken = default);

    string CreateFingerprint(string providerToken);
}

/// <param name="IsProtected">False when no usable key was available.</param>
public sealed record ProviderTokenProtectionResult(
    bool IsProtected,
    ProtectedProviderToken? Token)
{
    public static readonly ProviderTokenProtectionResult Failed =
        new(false, null);
}

/// <param name="IsRead">False when the token could not be recovered.</param>
public sealed record ProviderTokenReadResult(
    bool IsRead,
    string ProviderToken)
{
    public static readonly ProviderTokenReadResult Failed =
        new(false, string.Empty);
}
