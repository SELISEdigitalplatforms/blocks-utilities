namespace Payment.DomainService.Enums;

public enum WebhookSignatureOutcome
{
    /// <summary>The signature matched a configured secret.</summary>
    Valid = 0,

    /// <summary>The signature did not match any configured secret.</summary>
    Invalid = 1,

    /// <summary>
    /// The signature matched but the event is outside the provider's replay window. Distinct
    /// from <see cref="Invalid"/> because it means a genuine event arrived too late, not that
    /// someone forged one.
    /// </summary>
    Expired = 2,

    /// <summary>The provider has no secret configured for this signature, so nothing can be verified.</summary>
    NotConfigured = 3
}
