namespace Payment.DomainService.Providers;

public enum PaymentCaptureProviderOutcome
{
    /// <summary>Accepted, and settled later by a webhook naming the capture.</summary>
    Submitted,

    /// <summary>
    /// Already complete when the call returned, with no event to follow.
    /// </summary>
    /// <remarks>
    /// Stripe captures the payment intent in place and has no capture object, so nothing it
    /// later sends can identify which capture settled. Reporting this as merely submitted
    /// would leave the capture waiting for an event that cannot exist.
    /// </remarks>
    Settled,
    Rejected,
    Timeout,
    OutcomeUnknown,
    Unavailable
}
