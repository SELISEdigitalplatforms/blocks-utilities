using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Dtos
{
    [BsonIgnoreExtraElements]
    public class SsoInfo
    {
        [BsonId]
        public string ItemId { get; set; }
        public bool IsDisabled { get; set; }
        public string Provider { get; set; }
        public string Audience { get; set; }
    }
}
