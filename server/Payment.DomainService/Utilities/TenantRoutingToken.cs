namespace Payment.DomainService.Utilities;

public static class TenantRoutingToken
{
    private const int GuidByteCount = 16;

    public static bool TryEncode(
        string tenantId,
        out string token)
    {
        token = string.Empty;

        if (Guid.TryParseExact(tenantId, "N", out var compactGuid))
        {
            token = $"n{Encode(compactGuid)}";
            return true;
        }

        if (Guid.TryParseExact(tenantId, "D", out var dashedGuid))
        {
            token = $"d{Encode(dashedGuid)}";
            return true;
        }

        if (tenantId.Length == 33 &&
            char.IsLetterOrDigit(tenantId[0]) &&
            Guid.TryParseExact(tenantId[1..], "N", out var prefixedGuid))
        {
            token = $"p{tenantId[0]}{Encode(prefixedGuid)}";
            return true;
        }

        return false;
    }

    public static bool TryDecode(
        string? token,
        out string tenantId)
    {
        tenantId = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (token.Length == 23 &&
            token[0] is 'n' or 'd' &&
            TryDecodeGuid(token[1..], out var guid))
        {
            tenantId = guid.ToString(token[0] == 'n' ? "N" : "D");
            return true;
        }

        if (token.Length == 24 &&
            token[0] == 'p' &&
            char.IsLetterOrDigit(token[1]) &&
            TryDecodeGuid(token[2..], out guid))
        {
            tenantId = $"{token[1]}{guid:N}";
            return true;
        }

        return false;
    }

    private static string Encode(Guid value) =>
        Convert.ToBase64String(value.ToByteArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryDecodeGuid(
        string value,
        out Guid guid)
    {
        guid = default;

        try
        {
            var base64 = value
                .Replace('-', '+')
                .Replace('_', '/');
            var padding = (4 - base64.Length % 4) % 4;
            var bytes = Convert.FromBase64String(base64.PadRight(base64.Length + padding, '='));

            if (bytes.Length != GuidByteCount)
            {
                return false;
            }

            guid = new Guid(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
