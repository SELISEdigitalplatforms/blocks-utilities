using Iam.DomainService.Enums;

namespace Iam.DomainService.Dtos
{
    public class CreateUserViaSsoEvent
    {
        public required string ItemId { get; set; }
        public required MutationEventType Action { get; set; }
        public string? MailPurpose { get; set; }
        public bool SendWelcomeMail { get; set; } = true;
        public required string ProjectKey { get; set; }
    }
}
