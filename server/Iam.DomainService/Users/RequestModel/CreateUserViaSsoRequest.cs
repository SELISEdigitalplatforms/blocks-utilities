using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Users
{
    public class CreateUserViaSsoRequest : IProjectKey
    {
        public string? Language { get; set; } = "en-US";
        public required string Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? MailPurpose { get; set; }
        public bool SendWelcomeMail { get; set; } = true;
        public UserCreationType UserCreationType { get; set; } = UserCreationType.Social;
        public required string Platform { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageId { get; set; }
        public List<UserLogInType> AllowedLogInType { get; set; } = new List<UserLogInType> { UserLogInType.SSO };
        public List<OrganizationMembership> Memberships { get; set; } = [];
        public List<string> Permissions { get; set; } = new List<string>();
        public required string ProjectKey { get; set; }
        public bool Active { get; set; } = true;
        public bool IsVarified { get; set; } = true;
        public string? ExternalUserId { get; set; }
        public string? DepartMent { get; set; }
        public string? EmployeeId { get; set; }
    }
}
