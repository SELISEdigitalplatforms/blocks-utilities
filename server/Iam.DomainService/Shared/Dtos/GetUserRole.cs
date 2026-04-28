using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Dtos
{
    [BsonIgnoreExtraElements]
    public class GetUserRole
    {
        [BsonId]
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public string Description { get; set; }
        public long Count { get; set; }
    }
}
