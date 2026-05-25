using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public enum EmailTriggerType
    {
        Inbound,
        Outbound
    }

    public record EmailTriggerEvent
    {
        public required EmailTriggerType Type { get; set; }
        public required string ProjectKey { get; set; }
        public required MailBoxEntity Mail { get; set; }
    }

}