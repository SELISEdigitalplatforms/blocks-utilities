using Blocks.Genesis;

namespace Iam.DomainService.Accounts
{
    public class ChangePasswordRequest : IProjectKey
    {
        public string NewPassword { get; set; }
        public string OldPassword { get; set; }
        public string? ProjectKey { get; set; }
    }


}
