using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Cloud.DomainService.Models
{
    [BsonIgnoreExtraElements]
    public class ApiEndpointConfig : BaseEntity
    {
        [BsonElement("Name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("Type")]
        public int Type { get; set; }

        [BsonElement("Description")]
        public string? Description { get; set; }

        [BsonElement("Resource")]
        public string Resource { get; set; } = string.Empty;

        [BsonElement("ResourceGroup")]
        public string ResourceGroup { get; set; } = string.Empty;

        [BsonElement("IsBuiltIn")]
        public bool IsBuiltIn { get; set; }

        [BsonElement("IsArchived")]
        public bool IsArchived { get; set; }

        [BsonElement("DependentPermissions")]
        public List<string> DependentPermissions { get; set; } = new();

        [BsonElement("Roles")]
        public List<string> Roles { get; set; } = new();

        [BsonElement("UserId")]
        public List<string> UserId { get; set; } = new();

        [BsonElement("IsCaptchaRequired")]
        public bool IsCaptchaRequired { get; set; }

        [BsonElement("IsMFARequired")]
        public bool IsMFARequired { get; set; }

        [BsonElement("MfaMediaType")]
        public int MfaMediaType { get; set; }

        [BsonElement("IsAllowed")]
        public bool IsAllowed { get; set; }

        [BsonElement("Limit")]
        public int Limit { get; set; }

        [BsonElement("Usage")]
        public int Usage { get; set; }

        [BsonElement("BaseUrl")]
        public string BaseUrl { get; set; } = string.Empty;

        [BsonElement("Version")]
        public string Version { get; set; } = string.Empty;
    } 
}
