using System.Net;
using System.Net.Sockets;

namespace Payment.DomainService.Utilities;

/// <summary>
/// Shared transport-safety gate for every outbound payment URL. Rejects non-HTTPS schemes,
/// embedded credentials, loopback and private address space, so that no per-provider
/// allowlist can accidentally admit an SSRF target. Provider allowlists narrow this further;
/// none of them may bypass it.
/// </summary>
public static class SafeHttpsUrl
{
    public static bool TryParse(string? value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.IsLoopback)
        {
            return false;
        }

        return !IPAddress.TryParse(uri.Host, out var address) || IsPublic(address);
    }

    private static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal ||
                address.IsIPv6SiteLocal ||
                address.IsIPv6Multicast ||
                address.IsIPv6Teredo)
            {
                return false;
            }

            var ipv6 = address.GetAddressBytes();
            if ((ipv6[0] & 0xFE) == 0xFC) return false;
        }

        var bytes = address.MapToIPv4().GetAddressBytes();
        return !(bytes[0] == 10 || bytes[0] == 127 ||
                 bytes[0] == 169 && bytes[1] == 254 ||
                 bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                 bytes[0] == 192 && bytes[1] == 168);
    }
}
