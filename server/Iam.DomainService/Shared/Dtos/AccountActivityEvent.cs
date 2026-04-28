namespace Iam.DomainService.Dtos
{
    public class AccountActivityEvent
    {
        public string Code { get; set; }
        public string UserId { get; set; }
        public string Event { get; set; }
        public bool PreventPostEvent { get; set; }
        public string? MailPurpose { get; set; }
    }
}
