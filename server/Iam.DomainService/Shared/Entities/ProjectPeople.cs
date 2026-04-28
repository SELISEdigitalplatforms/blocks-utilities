using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Shared.Entities
{
    [BsonIgnoreExtraElements]
    public class ProjectPeople
    {
        [BsonId]
        public string ItemId { get; set; }
        public string UserId { get; set; }
        public string TenantId { get; set; }
    }
}
