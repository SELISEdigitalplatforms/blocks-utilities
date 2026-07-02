namespace Mail.DomainService.Mails.Services.Attachments
{
    public sealed record MailAttachmentMetadata(string FileId, long? SizeInBytes);
}
