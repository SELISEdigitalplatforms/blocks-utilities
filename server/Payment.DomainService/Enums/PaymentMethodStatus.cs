namespace Payment.DomainService.Enums;

public enum PaymentMethodStatus
{
    Active = 0,
    Removed = 1,
    RemovalPending = 2,
    RemovalOutcomeUnknown = 3,
    RemovalRequiresAttention = 4
}
