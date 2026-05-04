using System.Text.Json.Serialization;
using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Mail.DomainService.Mails
{
    public class GetMailBoxMailsResponse : BaseResponse
    {
        public long TotalCount { get; set; }
        public List<MailBoxEntityResponse> Mails { get; set; }
    }

    public class MailBoxEntityResponse : MailBoxEntity
    {
        public List<MailBoxEntityTimeline> Timeline { get; set; }
    }
    public class MailBoxEntityTimeline
    {
        [BsonRepresentation(BsonType.String)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MailStatus Status { get; set; }
        public DateTime Date { get; set; }
    }

}