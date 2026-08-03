using Payment.DomainService.Entities;
using Payment.DomainService.Models;

namespace Payment.DomainService.Providers;

public interface IProviderSecretReader
{
    /// <summary>
    /// Decrypts and deserialises both credential blobs, under the key ring belonging to the
    /// provider's own tenant and organization. Returns a failed result with a safe reason when
    /// anything is missing or unreadable; callers must fail closed.
    /// </summary>
    Task<ProviderSecretReadResult<TCredential>> ReadAsync<TCredential>(
        PaymentProvider provider,
        CancellationToken cancellationToken = default)
        where TCredential : class;
}

/// <param name="IsRead">False when the blobs are absent or cannot be decrypted.</param>
/// <param name="FailureReason">
/// A reason safe to log: <c>secrets_missing</c> or <c>secrets_unreadable</c>. Never the cause,
/// which would distinguish a wrong key from a tampered payload.
/// </param>
public sealed record ProviderSecretReadResult<TCredential>(
    bool IsRead,
    TCredential? Credentials,
    TenantPaymentSecuritySecret? TenantSecurity,
    string FailureReason)
    where TCredential : class
{
    public static ProviderSecretReadResult<TCredential> Failed(
        string failureReason) =>
        new(false, null, null, failureReason);
}
