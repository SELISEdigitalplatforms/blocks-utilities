using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class User : BaseEntity  
    {
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }
        public List<OrganizationMembership> Memberships { get; set; } = [];
        public bool Active { get; set; }
        public bool IsVarified { get; set; }
        public UserVarifiedType VarifiedType { get; set; } = UserVarifiedType.None;
        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageId { get; set; }
        public string? Platform { get; set; }
        public UserCreationType UserCreationType { get; set; } = UserCreationType.None;
        public UserPassType UserPassType { get; set; } = UserPassType.None;
        public string? Password { get; set; }
        public DateTime PasswordSetTime { get; set; }
        public UserMfaType UserMfaType { get; set; } = UserMfaType.None;
        public bool MfaEnabled { get; set; }
        public DateTime FirstLoggedInTime { get; set; }
        public DateTime LastLoggedInTime { get; set; }
        public string LastLoggedInDeviceInfo { get; set; } = string.Empty;
        public int LogInCount { get; set; }
        public List<UserLogInType> AllowedLogInType { get; set; } = new List<UserLogInType>();
        public bool IsDefault { get; set; }
        public string? MailPurpose { get; set; }
        public bool IsMfaVerified { get; set; }
        public string? ExternalUserId { get; set; }
        public string? Department { get; set; }
        public string? EmployeeId { get; set; }
    }

    public enum UserVarifiedType
    {
        None,
        Email,
        Sms, 
        WhatsApp
    }

    public enum UserCreationType
    {
        None,
        Portal,
        Api,
        Service,
        Social,
        ThirdParty,
    }

    public enum UserPassType
    {
        None,
        Password,
        Pin
    }

    public enum UserMfaType
    {
        None,
        TOTP,
        Email,
        Sms,
        WhatsApp,
        
    }
    public enum UserLogInType
    {
        None,
        Password,
        SSO,
        AuthrizationCode
    }
}
