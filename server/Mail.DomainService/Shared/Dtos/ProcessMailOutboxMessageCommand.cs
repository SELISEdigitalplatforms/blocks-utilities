namespace Mail.DomainService.Dtos
{
    public class ProcessMailOutboxMessageCommand
    {
        public string OutboxMessageId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectKey { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
        public DateTime NotBeforeUtc { get; set; } = DateTime.UtcNow;
    }
}
