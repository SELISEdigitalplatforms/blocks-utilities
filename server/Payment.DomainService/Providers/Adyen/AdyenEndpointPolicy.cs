using System.Text.RegularExpressions;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Adyen;

/// <summary>
/// Restricts Adyen calls to Adyen-owned hosts on a Checkout API version this service knows how
/// to speak. Earlier versions differ in request and notification shape, so they are refused
/// rather than attempted.
/// </summary>
public sealed partial class AdyenEndpointPolicy : IProviderEndpointPolicy
{
    private const int MinimumCheckoutApiVersion = 72;

    private static readonly string[] AllowedHostSuffixes =
    [
        ".adyen.com",
        ".adyenpayments.com"
    ];

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

    public bool IsAllowed(string? endpointUrl)
    {
        if (!SafeHttpsUrl.TryParse(endpointUrl, out var uri)) return false;

        var isAdyenHost = AllowedHostSuffixes.Any(suffix =>
            uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (!isAdyenHost) return false;

        var match = CheckoutApiVersionPattern().Match(uri.AbsolutePath);

        return match.Success &&
               int.TryParse(match.Groups["version"].Value, out var version) &&
               version >= MinimumCheckoutApiVersion;
    }

    [GeneratedRegex(
        @"/v(?<version>\d+)(?:/|$)",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex CheckoutApiVersionPattern();
}
