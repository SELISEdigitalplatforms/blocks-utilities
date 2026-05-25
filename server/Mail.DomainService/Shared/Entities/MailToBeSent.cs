using MongoDB.Bson.Serialization.Attributes;

namespace Mail.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class MailToBeSent
    {
        [BsonId]
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Language { get; set; }
        public bool SubjectTemplated { get; set; }
        public IEnumerable<string> Attachments { get; set; }
        public IEnumerable<string> Cc { get; set; }
        public IEnumerable<string> To { get; set; }
        public IEnumerable<string> Bcc { get; set; }
        public IEnumerable<string> ReplyTo { get; set; }

        public Dictionary<string, string> BodyDataContext { get; set; }
        public Dictionary<string, string> SubjectDataContext { get; set; }



        public string TextSubject { get; set; }


        public string BodyTemplateFileId { get; set; }
        public string SubjectTemplateFileId { get; set; }


        public EmailTemplate EmailTemplate { get; set; }
        public MailServerConfiguration MailServerConfiguration { get; set; }

        public bool IsTestMail { get; set; }
    }
}
