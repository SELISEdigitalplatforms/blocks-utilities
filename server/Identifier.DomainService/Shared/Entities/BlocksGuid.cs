
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Shared
{
    public class BlocksGuid
    {
        [BsonId]
        public string ItemId { get; set; } 
        public string TenantGroupId { get; set; }
        public string OriginalValue { get; set; }
        public string EncodedValue { get; set; }
    }
}
