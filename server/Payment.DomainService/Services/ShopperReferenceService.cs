using System.Security.Cryptography;
using System.Text;

namespace Payment.DomainService.Services;

public sealed class ShopperReferenceService : IShopperReferenceService
{
    public bool TryCreate(string tenantId, string actorId, string key, out string shopperReference)
    {
        shopperReference = string.Empty;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(key)) return false;
        var keyBytes = Encoding.UTF8.GetBytes(key);
        if (keyBytes.Length < 32) return false;
        var data = Encoding.UTF8.GetBytes($"{tenantId}:{actorId}");
        shopperReference = Convert.ToHexString(HMACSHA256.HashData(keyBytes, data)).ToLowerInvariant();
        return true;
    }
}
