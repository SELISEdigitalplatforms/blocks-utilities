using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

public interface IProviderTokenProtector
{
    bool TryProtect(
        string providerToken,
        out ProtectedProviderToken protectedToken);

    bool TryUnprotect(
        StoredPaymentMethod method,
        out string providerToken);

    string CreateFingerprint(string providerToken);
}
