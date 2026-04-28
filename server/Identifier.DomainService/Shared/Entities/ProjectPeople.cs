using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class ProjectPeople : BaseEntity
    {
        public string UserId { get; set; }
        public string Email { get; set; }
        public string TenantId { get; set; }
        public bool IsInvitationSent { get; set; }
        public bool IsInvitationConfirmed { get; set; }
        public bool IsCreator { get; set; }

    }

}
