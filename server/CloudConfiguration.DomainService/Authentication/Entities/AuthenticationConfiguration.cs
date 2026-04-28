using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CloudConfiguration.DomainService.Authentication.Entities
{
    [BsonIgnoreExtraElements]
    public class AuthenticationConfiguration
    {
        [BsonId]
        public ObjectId ItemId { get; set; }
        public List<string> AllowedGrantTypes { get; set; } = [];
        public int AccessTokenValidForNumberMinutes { get; init; } = 7;
        public int RefreshTokenValidForNumberMinutes { get; set; } = 30;
        public int RememberMeRefreshTokenValidForNumberMinutes { get; init; } = 30 * 60 * 24;
        public int GetNumberOfWrongAttemptsToLockTheAccount { get; set; } = 5;
        public int AccountLockDurationInMinutes { get; set; } = 5;
        public string PublicCertificatePath { get; set; } 
    }
}
