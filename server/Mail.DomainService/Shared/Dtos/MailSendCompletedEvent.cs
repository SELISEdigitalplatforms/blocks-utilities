using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Dtos
{
    public class MailSendCompletedEvent
    {
        public string ItemId { get; set; } = string.Empty;
        public string ProjectKey { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public MailCategory MailCategory { get; set; }
        public bool IsSuccess { get; set; }
        public DateTime CompletedAtUtc { get; set; }
        public int RecipientCount { get; set; }
        public int AttachmentCount { get; set; }
        public bool IsTestMail { get; set; }
        public string? FailureReason { get; set; }
    }
}
