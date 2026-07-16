using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payment.DomainService.Services;

public sealed class CheckoutCallbackStateProtector : ICheckoutCallbackStateProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProtectedCheckoutCallbackState Create(
        string tenantId,
        string paymentId,
        string providerName,
        TimeSpan lifetime,
        string key)
    {
        var now = DateTime.UtcNow;
        var state = new CheckoutCallbackState(
            tenantId,
            paymentId,
            providerName,
            now,
            now.Add(lifetime),
            Base64Url(RandomNumberGenerator.GetBytes(24)));
        var payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var signature = Sign(payload, DecodeKey(key));
        return new ProtectedCheckoutCallbackState($"{Base64Url(payload)}.{Base64Url(signature)}", state);
    }

    public bool TryRead(string token, out CheckoutCallbackState state)
    {
        state = default!;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return false;
        var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !TryDecodeBase64Url(parts[0], out var payload) || payload.Length > 2048) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<CheckoutCallbackState>(payload, JsonOptions);
            if (parsed == null || !IsSafeIdentifier(parsed.TenantId) || !IsSafeIdentifier(parsed.PaymentDetailId) ||
                !IsSafeIdentifier(parsed.ProviderName) || string.IsNullOrWhiteSpace(parsed.Nonce)) return false;
            state = parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }

    public bool TryUnprotect(string token, string activeKey, string? previousKey, out CheckoutCallbackState state)
    {
        if (!TryRead(token, out state)) return false;
        var parts = token.Split('.');
        if (!TryDecodeBase64Url(parts[0], out var payload) || !TryDecodeBase64Url(parts[1], out var supplied)) return false;
        var valid = Verify(payload, supplied, activeKey) ||
                    !string.IsNullOrWhiteSpace(previousKey) && Verify(payload, supplied, previousKey);
        var now = DateTime.UtcNow;
        return valid && state.ExpiresAtUtc >= now && state.IssuedAtUtc <= now.AddMinutes(5) && state.ExpiresAtUtc > state.IssuedAtUtc;
    }

    private static bool Verify(byte[] payload, byte[] supplied, string key)
    {
        try
        {
            var expected = Sign(payload, DecodeKey(key));
            return expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException) { return false; }
    }

    private static byte[] DecodeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("Missing key.");
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length >= 32) return bytes;
        }
        catch (FormatException) { }
        if (value.Length % 2 == 0 && value.All(Uri.IsHexDigit))
        {
            var bytes = Convert.FromHexString(value);
            if (bytes.Length >= 32) return bytes;
        }
        var utf8 = Encoding.UTF8.GetBytes(value);
        return utf8.Length >= 32 ? utf8 : throw new FormatException("HMAC keys must contain at least 256 bits.");
    }

    private static byte[] Sign(byte[] payload, byte[] key) => HMACSHA256.HashData(key, payload);
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool TryDecodeBase64Url(string value, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            bytes = Convert.FromBase64String(padded);

            if (string.Equals(Base64Url(bytes), value, StringComparison.Ordinal))
            {
                return true;
            }

            bytes = [];
            return false;
        }
        catch (FormatException) { return false; }
    }

    private static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.');
}
