namespace Payment.DomainService.Providers;

public sealed class WebhookSignatureVerifierResolver :
    IWebhookSignatureVerifierResolver
{
    private readonly IReadOnlyCollection<
        IWebhookSignatureVerifier> _verifiers;

    public WebhookSignatureVerifierResolver(
        IEnumerable<IWebhookSignatureVerifier> verifiers)
    {
        _verifiers = verifiers.ToArray();
    }

    public IWebhookSignatureVerifier? Resolve(string providerName) =>
        _verifiers.FirstOrDefault(verifier =>
            verifier.Supports(providerName));
}
