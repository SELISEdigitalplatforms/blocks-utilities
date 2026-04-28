using Iam.DomainService.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Dtos
{
    [BsonIgnoreExtraElements]
    public class GetUser : GetAccounts
    {
        public DateTime LastLoggedInTime { get; set; }
        public string LastLoggedInDeviceInfo { get; set; } = string.Empty;
        public int LogInCount { get; set; }    
    }
}
