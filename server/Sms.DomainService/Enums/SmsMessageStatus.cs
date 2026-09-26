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
    Quarantined = 9,
    RetryScheduled = 10,
    PartiallyDelivered = 11
}

public enum SmsRecipientStatus
{
    Pending = 1,
    Submitted = 2,
    Delivered = 3,
    Undelivered = 4,
    DeliveryFailed = 5,
    Failed = 6
}

public enum SmsUrlPolicy
{
    Allow = 1,
    Flag = 2,
    Block = 3
}
