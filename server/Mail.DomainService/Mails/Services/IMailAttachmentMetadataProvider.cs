namespace Mail.DomainService.Mails
{
    public interface IMailAttachmentMetadataProvider
    {
        Task<MailAttachmentMetadata> GetMetadataAsync(string fileId, CancellationToken cancellationToken = default);
    }
}
