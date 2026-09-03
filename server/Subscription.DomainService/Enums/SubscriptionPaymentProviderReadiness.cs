namespace Subscription.DomainService.Enums;

/// <summary>
/// Whether a tenant's configuration for a payment provider is ready to route money through.
/// </summary>
/// <remarks>
/// Backs three callers alike -- the merchant-profile GET (so the console can show readiness for
/// every registered provider at once), merchant-profile save validation, and the pre-persist
/// check subscription creation runs before it resolves which provider a new subscription is
/// pinned to. One evaluation, reused everywhere the question is asked, so "ready" cannot mean
/// something different in one caller than in another.
/// </remarks>
public enum SubscriptionPaymentProviderReadiness
{
    Ready,

    /// <summary>Not one of the providers this build knows how to route payments through.</summary>
    Unsupported,

    /// <summary>No configuration document exists for this tenant (or organization) and provider.</summary>
    NotConfigured,

    /// <summary>A configuration exists but has been switched off.</summary>
    Disabled,

    /// <summary>A configuration exists and is enabled, but is missing a base URL or merchant id.</summary>
    Misconfigured,

    /// <summary>
    /// Secrets could not be hydrated, or a required secret this provider needs is absent -- the
    /// webhook secret for Stripe; the API key and both webhook HMAC keys for Adyen.
    /// </summary>
    CredentialsUnavailable
}
