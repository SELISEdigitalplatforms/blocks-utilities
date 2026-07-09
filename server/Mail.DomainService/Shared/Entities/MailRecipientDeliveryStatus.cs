using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Entities
{
    public class MailRecipientDeliveryStatus
    {
        public string Recipient { get; set; } = string.Empty;
        public MailStatus Status { get; set; } = MailStatus.Pending;
        public string? StatusReason { get; set; }
        public DateTime? CheckedAtUtc { get; set; }
    }
}
