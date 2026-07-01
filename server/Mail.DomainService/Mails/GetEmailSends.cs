using Blocks.Genesis;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails
{
    public class GetEmailSends
    {
        public int PageSize { get; set; } = 25;
        public string? ContinuationToken { get; set; }
        public string? OrganizationId { get; set; }
        public MailSubmissionStatus? SubmissionStatus { get; set; }
        public string? SenderAddress { get; set; }
        public string? Subject { get; set; }
        public string? Language { get; set; }
        public string? RecipientAddress { get; set; }
        public DateTime? CreatedFromUtc { get; set; }
        public DateTime? CreatedToUtc { get; set; }
        public DateTime? SubmittedFromUtc { get; set; }
        public DateTime? SubmittedToUtc { get; set; }
    }

    public class GetEmailSendsResponse : BaseResponse
    {
        public IReadOnlyList<EmailSendListItem> Items { get; set; } = [];
        public string? NextContinuationToken { get; set; }
        public bool HasMore { get; set; }
        public int PageSize { get; set; }
    }

    public class EmailSendListItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string SenderAddress { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
        public MailSubmissionStatus SubmissionStatus { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? SubmittedAtUtc { get; set; }
        public MailCategory MailCategory { get; set; }
        public IReadOnlyList<EmailSendRecipientStatus> Recipients { get; set; } = [];
    }

    public class EmailSendRecipientStatus
    {
        public string Address { get; set; } = string.Empty;
        public string RecipientType { get; set; } = string.Empty;
        public MailStatus DeliveryStatus { get; set; }
        public string? StatusReason { get; set; }
        public DateTime? CheckedAtUtc { get; set; }
    }

    public class EmailSendQueryResult
    {
        public IReadOnlyList<Mail.DomainService.Entities.MailToBeSent> Items { get; set; } = [];
        public bool HasMore { get; set; }
    }
}
