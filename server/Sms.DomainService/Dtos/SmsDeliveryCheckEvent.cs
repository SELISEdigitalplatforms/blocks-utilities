namespace Sms.DomainService.Dtos;

public class SmsDeliveryCheckEvent
{
    public string MessageId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ProjectKey { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
    public DateTime NotBeforeUtc { get; set; } = DateTime.UtcNow;
}
