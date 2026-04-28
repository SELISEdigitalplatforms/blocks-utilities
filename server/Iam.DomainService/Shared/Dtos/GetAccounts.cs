using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Dtos
{
    [BsonIgnoreExtraElements]
    public class GetAccounts
    {
        [BsonId]
        public string ItemId { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdatedDate { get; set; }
        public string? Language { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string Email { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }
        public List<OrganizationMembership> Memberships { get; set; } = [];
        public bool Active { get; set; }
        public bool IsVarified { get; set; }
        public string? ProfileImageUrl { get; set; }
        public bool MfaEnabled { get; set; }
        public bool IsMfaVerified { get; set; }
        public UserMfaType UserMfaType { get; set; }
        public UserCreationType UserCreationType { get; set; }
        public string? Department { get; set; }
        public string? EmployeeId { get; set; }
    }
}
