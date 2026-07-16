using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class CheckoutUrlPolicy : ICheckoutUrlPolicy
{
    public bool TryResolveHostedUrls(PaymentProvider provider, string signedState, out string returnUrl, out string frontendResultUrl)
    {
        returnUrl = string.Empty;
        frontendResultUrl = string.Empty;
        if (!TryGetSafeHttpsUri(provider.ReturnUrl, out var backend) ||
            !TryGetSafeHttpsUri(provider.FrontendResultUrl, out var frontend)) return false;

        var builder = new UriBuilder(backend);
        var query = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(query)
            ? $"state={Uri.EscapeDataString(signedState)}"
            : $"{query}&state={Uri.EscapeDataString(signedState)}";
        returnUrl = builder.Uri.AbsoluteUri;
        frontendResultUrl = frontend.AbsoluteUri;
        return true;
    }

    public bool IsAllowedFrontendResultUrl(string value) => TryGetSafeHttpsUri(value, out _);

    public bool IsAllowedProviderEndpoint(string value)
    {
        if (!TryGetSafeHttpsUri(value, out var uri)) return false;
        var isApprovedProviderHost =
            uri.Host.EndsWith(".adyen.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".adyenpayments.com", StringComparison.OrdinalIgnoreCase);

        if (!isApprovedProviderHost) return false;
        var match = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, @"/v(?<version>\d+)(?:/|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["version"].Value, out var version) && version >= 72;
    }

    private static bool TryGetSafeHttpsUri(string? value, out Uri uri)
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
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6Teredo) return false;
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
