namespace Sms.DomainService.Enums;

public enum SmsOutboxStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    RetryScheduled = 4,
    Failed = 5
}
