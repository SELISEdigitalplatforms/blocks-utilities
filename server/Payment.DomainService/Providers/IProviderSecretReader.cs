using Payment.DomainService.Entities;
using Payment.DomainService.Models;

namespace Payment.DomainService.Providers;

public interface IProviderSecretReader
{
    /// <summary>
    /// Decrypts and deserialises both credential blobs. Returns <see langword="false"/> with a
    /// safe reason when anything is missing or unreadable; callers must fail closed.
    /// </summary>
    bool TryRead<TCredential>(
        PaymentProvider provider,
        out TCredential? credentials,
        out TenantPaymentSecuritySecret? tenantSecurity,
        out string failureReason)
        where TCredential : class;
}
