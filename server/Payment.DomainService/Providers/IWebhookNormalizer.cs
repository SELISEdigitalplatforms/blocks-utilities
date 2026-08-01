using Payment.DomainService.Models.Webhooks;

namespace Payment.DomainService.Providers;

/// <summary>
/// Reads one provider's raw webhook request into events the rest of the system understands.
/// </summary>
/// <remarks>
/// Returning a list lets providers that batch events into one request and providers that send
/// one event per request share a single contract, with neither treated as the special case.
/// Nothing here is trusted yet — parsing only establishes what the request claims.
/// </remarks>
public interface IWebhookNormalizer
{
    bool Supports(string providerName);

    WebhookParseResult Parse(
        string rawBody,
        IReadOnlyDictionary<string, string> headers);
}
