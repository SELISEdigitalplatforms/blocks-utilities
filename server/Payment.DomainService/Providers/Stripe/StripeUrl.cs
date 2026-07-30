namespace Payment.DomainService.Providers.Stripe;

public static class StripeUrl
{
    /// <summary>
    /// Joins a Stripe API path onto the configured base. Path segments containing provider
    /// identifiers must be escaped by the caller before being interpolated.
    /// </summary>
    public static string Build(string apiBaseUrl, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiBaseUrl);

        var baseUri = new Uri(
            apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/");

        return new Uri(baseUri, path).AbsoluteUri;
    }
}
