using System.Security.Cryptography;
using System.Text;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Services;

public sealed class WebhookSignatureValidator : IWebhookSignatureValidator
{
    public bool ValidateStandard(NotificationItem item, string activeKey, string? previousKey)
    {
        if (!item.AdditionalData.TryGetValue("hmacSignature", out var supplied) || string.IsNullOrWhiteSpace(supplied)) return false;
        var canonical = string.Join(':', new[]
        {
            Escape(item.PspReference), Escape(item.OriginalReference), Escape(item.MerchantAccountCode),
            Escape(item.MerchantReference), item.Amount?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(item.Amount?.Currency), Escape(item.EventCode), Escape(item.Success)
        });
        return VerifyBase64(Encoding.UTF8.GetBytes(canonical), supplied, activeKey, hexKey: true) ||
               !string.IsNullOrWhiteSpace(previousKey) && VerifyBase64(Encoding.UTF8.GetBytes(canonical), supplied, previousKey, hexKey: true);
    }

    public bool ValidateToken(string rawBody, string suppliedSignature, string activeKey, string? previousKey) =>
        VerifyBase64(Encoding.UTF8.GetBytes(rawBody), suppliedSignature, activeKey, hexKey: false) ||
        !string.IsNullOrWhiteSpace(previousKey) && VerifyBase64(Encoding.UTF8.GetBytes(rawBody), suppliedSignature, previousKey, hexKey: false);

    private static bool VerifyBase64(byte[] data, string supplied, string key, bool hexKey)
    {
        try
        {
            var keyBytes = hexKey ? Convert.FromHexString(key) : DecodeFlexible(key);
            var expected = HMACSHA256.HashData(keyBytes, data);
            var actual = Convert.FromBase64String(supplied);
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }

    private static byte[] DecodeFlexible(string key)
    {
        if (key.Length % 2 == 0 && key.All(Uri.IsHexDigit))
        {
            return Convert.FromHexString(key);
        }

        try { return Convert.FromBase64String(key); }
        catch (FormatException) { return Encoding.UTF8.GetBytes(key); }
    }

    private static string Escape(string? value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace(":", "\\:");
}
