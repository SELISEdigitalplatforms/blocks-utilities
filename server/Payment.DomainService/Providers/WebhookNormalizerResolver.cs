namespace Payment.DomainService.Providers;

public sealed class WebhookNormalizerResolver : IWebhookNormalizerResolver
{
    private readonly IReadOnlyCollection<IWebhookNormalizer> _normalizers;

    public WebhookNormalizerResolver(
        IEnumerable<IWebhookNormalizer> normalizers)
    {
        _normalizers = normalizers.ToArray();
    }

    public IWebhookNormalizer? Resolve(string providerName) =>
        _normalizers.FirstOrDefault(normalizer =>
            normalizer.Supports(providerName));
}
