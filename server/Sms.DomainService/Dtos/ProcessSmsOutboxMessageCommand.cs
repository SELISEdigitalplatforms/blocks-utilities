namespace Sms.DomainService.Dtos;

public class ProcessSmsOutboxMessageCommand
{
    public string OutboxMessageId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ProjectKey { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime NotBeforeUtc { get; set; } = DateTime.UtcNow;
}
