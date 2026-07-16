using System.Security.Cryptography;
using System.Text;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class ShopperReferenceService : IShopperReferenceService
{
    private const string Version = "s1";

    public bool TryCreate(
        string tenantId,
        string actorId,
        string key,
        out string shopperReference)
    {
        shopperReference = string.Empty;

        if (string.IsNullOrWhiteSpace(actorId) ||
            string.IsNullOrWhiteSpace(key) ||
            !TenantRoutingToken.TryEncode(tenantId, out var tenantToken))
        {
            return false;
        }

        var keyBytes = Encoding.UTF8.GetBytes(key);
        if (keyBytes.Length < 32)
        {
            return false;
        }

        var data = Encoding.UTF8.GetBytes($"{tenantId}:{actorId}");
        var actorHash = Convert.ToHexString(
                HMACSHA256.HashData(keyBytes, data))
            .ToLowerInvariant();

        shopperReference = $"{Version}.{tenantToken}.{actorHash}";
        return true;
    }

    public bool TryResolveTenant(
        string? shopperReference,
        out string tenantId)
    {
        tenantId = string.Empty;

        if (string.IsNullOrWhiteSpace(shopperReference) ||
            shopperReference.Length > 256)
        {
            return false;
        }

        var parts = shopperReference.Split('.', 3, StringSplitOptions.None);
        return parts.Length == 3 &&
               string.Equals(parts[0], Version, StringComparison.Ordinal) &&
               parts[2].Length == 64 &&
               parts[2].All(Uri.IsHexDigit) &&
               TenantRoutingToken.TryDecode(parts[1], out tenantId);
    }
}
