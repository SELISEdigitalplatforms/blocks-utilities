using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class TenantCertificate : BaseEntity
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
