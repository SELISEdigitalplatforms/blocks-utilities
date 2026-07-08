namespace Sms.DomainService.Enums;

public enum SmsMessageStatus
{
    Accepted = 1,
    Queued = 2,
    Processing = 3,
    Submitted = 4,
    Delivered = 5,
    Undelivered = 6,
    DeliveryFailed = 7,
    Failed = 8,
    Quarantined = 9
}
