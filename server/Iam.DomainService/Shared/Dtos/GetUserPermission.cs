using Iam.DomainService.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Dtos
{
    [BsonIgnoreExtraElements]
    public class GetUserPermission
    {
        [BsonId]
        public string ItemId { get; set; }
        public string Name { get; set; }
        public ResourceType Type { get; set; }
        public string Description { get; set; }
        public string Resource { get; set; }
    }
}
