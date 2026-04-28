using Blocks.Genesis;

namespace Iam.DomainService.Accounts
{
    public class RecoveryUserRequest : IProjectKey
    {
        public string Email { get; set; }
        public string? CaptchaCode { get; set; }
        public string? MailPurpose { get; set; }
        public string? ProjectKey { get; set; }
    }


}
