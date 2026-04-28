using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class OIDCClientCredential : BaseEntity
    {
        public string ClientSecret { get; set; }
        public string? RedirectUri { get; set; }
        public string? Scope { get; set; }
        public string? Audience { get; set; }
        public bool IsAutoRedirect { get; set; }
        public string? ClientLogoUrl { get; set; }
        public string? ClientDisplayName { get; set; }
        public string? ClientBrandColor { get; set; }

    }
}
