namespace Payment.DomainService.Enums;

public enum PaymentOutboxStatus
{
    Pending,
    Processing,
    RetryScheduled,
    Published,
    DeadLettered
}
