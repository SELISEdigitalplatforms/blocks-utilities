using Iam.DomainService.Dtos;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class Session : RefreshTokenEvent
    {
        [BsonId]
        public ObjectId ItemId { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
        public bool IsActive { get; set; }
    }
}
