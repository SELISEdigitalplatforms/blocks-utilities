namespace Payment.DomainService.Providers;

public interface IWebhookSignatureVerifierResolver
{
    IWebhookSignatureVerifier? Resolve(string providerName);
}
