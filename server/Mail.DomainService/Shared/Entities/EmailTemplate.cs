using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Mail.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class EmailTemplate : BaseEntity
    {
        public string? MailConfigurationId { get; set; }
        public string? TemplateBody { get; set; }
        public string? JsonContent { get; set; }
        public string? ImageId { get; set; }
        public string? ImageUrl { get; set; }
        public string? Name { get; set; }
        public string? TemplateSubject { get; set; }
        public string? GeneratedBy { get; set; }
    }
}
