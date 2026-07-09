using Sms.DomainService.Enums;

namespace Sms.DomainService.Dtos;

public class SmsProviderResult
{
    public bool IsSuccess { get; set; }
    public bool IsTransientFailure { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static SmsProviderResult Submitted(string providerMessageId, string? providerStatus = null)
    {
        return new SmsProviderResult
        {
            IsSuccess = true,
            ProviderMessageId = providerMessageId,
            ProviderStatus = providerStatus
        };
    }

    public static SmsProviderResult Failed(string errorCode, string errorMessage, bool transient)
    {
        return new SmsProviderResult
        {
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            IsTransientFailure = transient
        };
    }
}

public class SmsProviderDeliveryStatus
{
    public bool IsFinal { get; set; }
    public SmsMessageStatus Status { get; set; } = SmsMessageStatus.Submitted;
    public string? ProviderStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
