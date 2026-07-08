using Sms.DomainService.Enums;

namespace Sms.DomainService.Dtos;

public sealed class SmsStatusEvent
{
    public string MessageId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ProjectKey { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public SmsProviderType? Provider { get; set; }
    public SmsMessageStatus Status { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
