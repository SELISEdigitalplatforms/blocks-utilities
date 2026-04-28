using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Users
{
    public class CreateUserRequest : IProjectKey
    {
        public string? Language { get; set; } = "en-US";
        public List<string>? Tags { get; set; }
        public string Email { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Password { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? MailPurpose { get; set; }
        public UserPassType UserPassType { get; set; } = UserPassType.Password;
        public UserCreationType UserCreationType { get; set; } = UserCreationType.Portal;
        public UserVarifiedType VarifiedType { get; set; } = UserVarifiedType.Email;
        public string? Platform { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageId { get; set; }
        public UserMfaType UserMfaType { get; set; } = UserMfaType.TOTP;
        public bool MfaEnabled { get; set; }
        public List<UserLogInType> AllowedLogInType { get; set; } = new List<UserLogInType> { UserLogInType.Password };
        public List<OrganizationMembership> Memberships { get; set; } = [];
        public string? ProjectKey { get; set; }
        public string? OrganizationId { get; set; }
    }

}
