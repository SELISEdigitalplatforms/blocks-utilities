using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace DomainService.Shared
{
    public class OfflineNotification
    {
        [BsonId]
        public string Id { get; set; }
        public string CorrelationId { get; set; }
        public PayloadData Payload { get; set; }

        [BsonIgnoreIfNull]
        public dynamic DenormalizedPayload { get; set; }
        public DateTime CreatedTime { get; set; }

        [BsonIgnoreIfNull]
        public List<string> ReadByUserIds { get; set; }
        public List<string> ReadByRoles { get; set; }

        [BsonIgnore]
        public bool IsRead { get; set; }
    }
}
