using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public interface IMailAttachmentProvider
    {
        Task<IReadOnlyList<MailAttachment>> GetAttachmentsAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default);
    }
}
