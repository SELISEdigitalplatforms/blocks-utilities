using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails.Services.Attachments
{
    public interface IMailAttachmentProvider
    {
        Task<IReadOnlyList<MailAttachment>> GetAttachmentsAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default);
    }
}
