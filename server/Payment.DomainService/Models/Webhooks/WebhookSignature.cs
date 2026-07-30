namespace Payment.DomainService.Models.Webhooks;

/// <summary>
/// Everything needed to check one inbound event's authenticity, with the provider-specific
/// question of *what* gets signed already answered by that provider's normalizer.
/// </summary>
/// <param name="SignedPayload">
/// The exact bytes the provider says are covered by the signature. Adyen builds a canonical
/// string from selected notification fields; Stripe uses "{timestamp}.{raw body}".
/// </param>
/// <param name="SuppliedSignature">The signature as it arrived, still encoded.</param>
/// <param name="SecretName">
/// Which of the provider's configured secrets applies, for providers that sign different
/// webhook kinds with different keys.
/// </param>
public sealed record WebhookSignature(
    string SignedPayload,
    string SuppliedSignature,
    string SecretName);
