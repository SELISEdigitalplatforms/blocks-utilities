namespace Iam.DomainService.Entities
{
    public class UserKeyMap
    {
        public string Key { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpireDate { get; set; }
        public string UserId { get; set; }
        public string MailPurpose { get; set; }
        public string Value { get; set; }
        public bool Activated { get; set; }
    }
}
