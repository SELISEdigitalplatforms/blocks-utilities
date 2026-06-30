namespace Mail.DomainService.Mails
{
    public sealed record MailAttachmentMetadata(string FileId, long? SizeInBytes);
}
