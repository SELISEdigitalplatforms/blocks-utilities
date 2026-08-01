namespace Payment.DomainService.Providers;

public enum PaymentRefundProviderOutcome
{
    /// <summary>Accepted, and settled later by an event naming this refund.</summary>
    Submitted,

    /// <summary>
    /// Already complete when the call returned, with no event to follow.
    /// </summary>
    /// <remarks>
    /// Cancelling an uncaptured authorization creates no object of its own at Stripe, so the
    /// event it raises names the payment rather than this reversal. Reporting it as merely
    /// submitted would leave the reversal waiting for an event that cannot identify it.
    /// </remarks>
    Settled,
    Rejected,
    Timeout,
    OutcomeUnknown,
    Unavailable
}
