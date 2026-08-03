using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payment.DomainService.Services;

public interface ICheckoutCallbackStateProtector
{
    ProtectedCheckoutCallbackState Create(string tenantId, string? organizationId, string paymentId, string providerName, TimeSpan lifetime, string key);
    bool TryRead(string token, out CheckoutCallbackState state);
    bool TryUnprotect(string token, string activeKey, string? previousKey, out CheckoutCallbackState state);
}
