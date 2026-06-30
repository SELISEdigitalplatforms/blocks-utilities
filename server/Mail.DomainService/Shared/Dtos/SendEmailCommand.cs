namespace Mail.DomainService.Dtos
{
    public class SendEmailCommand
    {
        public string ItemId { get; set; }
        public Mail.DomainService.Shared.Enums.MailCategory MailCategory { get; set; } = Mail.DomainService.Shared.Enums.MailCategory.NoAttachment;
    }

    public class NoAttachmentSendEmailCommand : SendEmailCommand
    {
        public NoAttachmentSendEmailCommand()
        {
            MailCategory = Mail.DomainService.Shared.Enums.MailCategory.NoAttachment;
        }
    }

    public class SmallAttachmentSendEmailCommand : SendEmailCommand
    {
        public SmallAttachmentSendEmailCommand()
        {
            MailCategory = Mail.DomainService.Shared.Enums.MailCategory.SmallAttachment;
        }
    }

    public class LargeAttachmentSendEmailCommand : SendEmailCommand
    {
        public LargeAttachmentSendEmailCommand()
        {
            MailCategory = Mail.DomainService.Shared.Enums.MailCategory.LargeAttachment;
        }
    }
}
