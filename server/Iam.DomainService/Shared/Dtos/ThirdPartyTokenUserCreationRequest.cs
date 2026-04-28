
namespace Iam.DomainService.Dtos
{
    public class ThirdPartyTokenUserCreationRequest
    {
        public string UserId { get; set; }
        public string? Language { get; set; } 
        public required string Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public bool SendWelcomeMail { get; set; }
        public List<string> Roles { get; set; }
        public List<string> Permissions { get; set; } 
        public required string ProjectKey { get; set; }
        public bool Active { get; set; }
        public bool IsVerified { get; set; } 
    }
}
