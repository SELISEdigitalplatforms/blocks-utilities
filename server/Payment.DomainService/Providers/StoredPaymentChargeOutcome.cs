namespace Payment.DomainService.Providers;

public enum StoredPaymentChargeOutcome
{
    Accepted,
    Rejected,
    Timeout,
    OutcomeUnknown,
    Unavailable
}
