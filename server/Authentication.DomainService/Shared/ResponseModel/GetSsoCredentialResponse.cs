using Blocks.Genesis;
using Iam.DomainService.Dtos;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.ResponseModel
{
    [BsonIgnoreExtraElements]
    public class GetSsoCredentialResponse : BaseEntity
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
        public required string Scope { get; set; }
        public List<GetUserRole> UserRoles { get; set; }
        public List<GetUserPermission> UserPermissions { get; set; }
        public bool IsDisabled { get; set; }
        public bool SendAsResponse { get; set; } = true;
    }
}
