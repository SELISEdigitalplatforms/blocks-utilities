using Blocks.Genesis;

namespace Iam.DomainService.Accounts
{
    public class ResendActivationRequest : IProjectKey
    {
        public string UserId { get; set; }
        public string? MailPurpose { get; set; }
        public string? ProjectKey { get; set; }
    }


}
