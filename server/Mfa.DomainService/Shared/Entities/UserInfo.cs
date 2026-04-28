using Iam.DomainService.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace Mfa.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class UserInfo
    {
        [BsonId]
        public string ItemId { get; set; }
        public string Email { get; set; }
        public bool MfaEnabled { get; set; }
        public string Language { get; set; }
        public string PhoneNumber { get; set; }
        public bool Active { get; set; }
        public UserMfaType UserMfaType { get; set; }
        public bool IsMfaVerified { get; set; }
    }
}
