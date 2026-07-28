using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.Webhooks;

namespace Payment.DomainService.Providers;

/// <summary>
/// Checks that an event really came from the provider it claims to.
/// </summary>
/// <remarks>
/// The normalizer has already decided what bytes are covered by the signature, so an
/// implementation only owns its provider's cryptographic details: which secret applies, how
/// the key and digest are encoded, and whether the provider enforces a replay window.
/// </remarks>
public interface IWebhookSignatureVerifier
{
    bool Supports(string providerName);

    WebhookSignatureOutcome Verify(
        PaymentProvider provider,
        WebhookSignature signature);
}
