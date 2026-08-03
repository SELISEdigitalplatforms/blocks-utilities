namespace Payment.DomainService.Providers;

public interface IWebhookNormalizerResolver
{
    IWebhookNormalizer? Resolve(string providerName);
}
