namespace Sms.DomainService.Enums;

public enum SmsFailureType
{
    None = 0,
    Transient = 1,
    Permanent = 2,
    SecurityRejected = 3,
    RateLimited = 4,
    ConfigurationMissing = 5
}
