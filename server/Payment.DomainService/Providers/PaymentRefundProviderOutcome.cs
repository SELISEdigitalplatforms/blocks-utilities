namespace Payment.DomainService.Providers;

public enum PaymentRefundProviderOutcome
{
    Submitted,
    Rejected,
    Timeout,
    OutcomeUnknown,
    Unavailable
}
