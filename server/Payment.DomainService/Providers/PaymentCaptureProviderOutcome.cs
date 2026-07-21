namespace Payment.DomainService.Providers;

public enum PaymentCaptureProviderOutcome
{
    Submitted,
    Rejected,
    Timeout,
    OutcomeUnknown,
    Unavailable
}
