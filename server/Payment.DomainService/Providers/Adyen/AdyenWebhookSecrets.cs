namespace Payment.DomainService.Providers.Adyen;

/// <summary>
/// Names the Adyen normalizer stamps on a signature so the verifier knows which configured
/// secret to check it against. Adyen signs its two webhook kinds with different keys.
/// </summary>
internal static class AdyenWebhookSecrets
{
    public const string Standard = "standard";
    public const string Token = "token";
}
