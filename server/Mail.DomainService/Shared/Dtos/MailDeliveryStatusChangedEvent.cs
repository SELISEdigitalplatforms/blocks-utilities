using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Dtos
{
    public class MailDeliveryStatusChangedEvent
    {
        public string ItemId { get; set; } = string.Empty;
        public string ProjectKey { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public MailStatus Status { get; set; }
        public string? StatusReason { get; set; }
        public DateTime CheckedAtUtc { get; set; }
    }
}
