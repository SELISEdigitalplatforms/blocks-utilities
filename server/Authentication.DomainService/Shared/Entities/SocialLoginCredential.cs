using Blocks.Genesis;
using DomainService.Shared;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class SocialLoginCredential : BaseEntity
    {
        public required string Provider { get; set; }
        public required string Audience { get; set; }
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        public required string AuthorizationUrl { get; set; }
        public required string TokenUrl { get; set; }
        public required string GetProfileUrl { get; set; }
        public required string RedirectUrl { get; set; }
        public string? WellKnownUrl { get; set; }
        public string? GetEmailUrl { get; set; }
        public required string Scope { get; set; }
        public List<string> InitialRoles { get; set; }
        public List<string> InitialPermissions { get; set; }
        public bool IsDisabled { get; set; }
        public bool SendAsResponse { get; set; } = true;
        public SSOType SSOType { get; set; }
        public string? TeamId { get; set; }
        public string? KeyId { get; set; }
        public string? PrivateKey { get; set; }
        public string? AppleAudience { get; set; }

    }
}
