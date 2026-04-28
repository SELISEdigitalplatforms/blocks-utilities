using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class IamConfiguration
    {
        [BsonId]
        public ObjectId ItemId {  get; set; }
        public string AccountActivationUrl { get; set; }
        public string AccountVerificationUrl { get; set; }
        public string RecoverAccountUrl { get; set; }
        public int ActivationUrlLifetimeInMinutes { get; set; } = 60 * 24; // Default 1 day
        public int RecoverAccountUrlLifetimeInMinutes { get; set; } = 10; // Default 10 mins
        public bool LogoutOnPasswordChange { get; set; } = true;
        public string PasswordStrengthCheckerRegex { get; set; }
    }
}
