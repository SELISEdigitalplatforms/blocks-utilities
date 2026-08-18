namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Checks that a file link taken from a Stripe response is served by Stripe.
/// </summary>
/// <remarks>
/// Invoice PDFs do not come from the API host, so <see cref="StripeEndpointPolicy"/> — which
/// guards the API base URL — says nothing about them. A URL read out of a response body is still
/// input: without this the credential-bearing fetch would follow wherever that body pointed,
/// which is the shape of a server-side request forgery even when the response is genuinely
/// Stripe's.
/// <para>
/// Matched as "any host within stripe.com" rather than a list of named hosts. The first version
/// named <c>files.stripe.com</c>, which is where File objects live — invoice PDFs are served from
/// <c>pay.stripe.com</c>, so every real invoice download was refused while a unit test using a
/// made-up <c>files.stripe.com</c> URL agreed it should be allowed. Enumerating Stripe's hosts
/// means being wrong again the next time they add one, and the property actually worth checking
/// is the domain.
/// </para>
/// </remarks>
public static class StripeFileUrl
{
    private const string Domain = "stripe.com";

    /// <summary>
    /// The leading dot is what makes this a subdomain test rather than a suffix test:
    /// <c>notstripe.com</c> and <c>files.stripe.com.evil.test</c> both fail it.
    /// </summary>
    private const string DomainSuffix = "." + Domain;

    public static bool IsStripeHosted(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        parsed.Scheme == Uri.UriSchemeHttps &&
        (parsed.Host.Equals(Domain, StringComparison.OrdinalIgnoreCase) ||
         parsed.Host.EndsWith(DomainSuffix, StringComparison.OrdinalIgnoreCase));
}
