namespace Iam.DomainService.Dtos
{
    public class SendMail
    {
        public Dictionary<string, string> SubjectDataContext { get; set; } = new Dictionary<string, string>();
        public IEnumerable<string> To { get; set; }
        public IEnumerable<string> Bcc { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> Cc { get; set; } = Enumerable.Empty<string>();
        public string Purpose { get; set; }
        public string Language { get; set; }
        public IEnumerable<string> ReplyTo { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> Attachments { get; set; } = Enumerable.Empty<string>();
        public Dictionary<string, string> BodyDataContext { get; set; } = new Dictionary<string, string>();
        public string ProjectKey { get; set; }
    }
}
