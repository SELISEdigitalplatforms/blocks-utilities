using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class TenantCertificate
    {
        [BsonId]
        public string ItemId { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
