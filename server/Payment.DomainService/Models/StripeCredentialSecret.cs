namespace Payment.DomainService.Models;

/// <summary>
/// Vault shape for Stripe. Stripe uses one secret API key for all calls and one signing
/// secret per webhook endpoint, so there is no per-webhook-kind split as with Adyen.
/// </summary>
public sealed class StripeCredentialSecret
{
    /// <summary>Restricted or standard secret key, <c>sk_...</c> or <c>rk_...</c>.</summary>
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// Endpoint signing secret, <c>whsec_...</c>. Rolling a secret keeps the previous one
    /// valid for up to 24 hours, so both are held.
    /// </summary>
    public RotatingPaymentSecret WebhookSigningSecret { get; init; } = new();
}
