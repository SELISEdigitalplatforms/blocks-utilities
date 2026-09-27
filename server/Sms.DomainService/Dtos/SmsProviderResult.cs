using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Dtos;

/// <summary>A tenant's configuration plus its provider key, resolved for one unit of work.</summary>
public sealed record SmsProviderContext(string TenantId, SmsProviderConfiguration Configuration, string ApiKey);

public class SmsProviderResult
{
    public bool IsSuccess { get; set; }
    public bool IsTransientFailure { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static SmsProviderResult Submitted(string providerMessageId) =>
        new() { IsSuccess = true, ProviderMessageId = providerMessageId };

    public static SmsProviderResult Failed(string errorCode, string errorMessage, bool transient) =>
        new() { ErrorCode = errorCode, ErrorMessage = errorMessage, IsTransientFailure = transient };
}

public class SmsProviderDeliveryStatus
{
    /// <summary>Null while the provider has no final answer yet.</summary>
    public SmsRecipientStatus? FinalStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed record SmsWebhookRequest(string RawBody, IReadOnlyDictionary<string, string> Headers);

public enum SmsWebhookVerdict
{
    Verified = 1,
    Unauthorized = 2,
    Malformed = 3
}

public sealed record SmsDeliveryCallback(string ProviderMessageId, SmsRecipientStatus? FinalStatus, string? ErrorCode, string? ErrorMessage);

public sealed record SmsWebhookParseResult(SmsWebhookVerdict Verdict, SmsDeliveryCallback? Callback = null);
