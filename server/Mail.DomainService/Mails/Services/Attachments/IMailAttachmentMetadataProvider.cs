namespace Mail.DomainService.Mails.Services.Attachments
{
    public interface IMailAttachmentMetadataProvider
    {
        Task<MailAttachmentMetadata> GetMetadataAsync(string fileId, CancellationToken cancellationToken = default);
    }
}
