namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Checks that a file link taken from a Stripe response is served by Stripe.
/// </summary>
/// <remarks>
/// Invoice PDFs come from <c>files.stripe.com</c>, not the API host, so
/// <see cref="StripeEndpointPolicy"/> — which guards the API base URL — says nothing about them.
/// A URL read out of a response body is still input: without this the credential-bearing fetch
/// would follow wherever that body pointed, which is the shape of a server-side request forgery
/// even when the response is genuinely Stripe's.
/// </remarks>
public static class StripeFileUrl
{
    private const string FileHost = "files.stripe.com";

    public static bool IsStripeHosted(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        parsed.Scheme == Uri.UriSchemeHttps &&
        (parsed.Host.Equals(FileHost, StringComparison.OrdinalIgnoreCase) ||
         parsed.Host.Equals(StripeConstants.ApiHost, StringComparison.OrdinalIgnoreCase));
}
