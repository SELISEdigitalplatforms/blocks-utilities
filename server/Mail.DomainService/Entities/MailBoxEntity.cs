using Mail.DomainService.Shared.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Mail.DomainService.Entities
{
    public class MailBoxEntity
    {
        [BsonId]
        public string ItemId { get; set; }
        public string MessageId { get; set; }
        public string? MailServerConfigurationId { get; set; } = String.Empty;
        public string Subject { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Body { get; set; }


        [BsonRepresentation(BsonType.String)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MailStatus Status { get; set; }
        
        public string Error { get; set; }
        public DateTime Date { get; set; }
        public string RawMime { get; set; }
        public bool IsInbound { get; set; }
    }
}