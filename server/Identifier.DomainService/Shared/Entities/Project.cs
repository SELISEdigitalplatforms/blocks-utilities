using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class Project : BaseEntity
    {
        public string Name { get; set; }
        public string ApplicationDomain { get; set; }
        public bool IsProduction { get; set; }
        public string TenantId { get; set; }
        public string TenantGroupId { get; set; }
        public bool IsDomainVerified { get; set; }
        public string CookieDomain { get; set; }
        public bool IsCookieEnable { get; set; }
        public string Environment { get; set; }
        public bool IsDisabled { get; set; }
        public string CustomDomain { get; set; }
    }
}
