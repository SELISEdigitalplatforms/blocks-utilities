using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class ThirdPartyJWTClaims : BaseEntity
    {
        public string UserId { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public string UserName { get; set; }
        public string Roles { get; set; }
    }
}
