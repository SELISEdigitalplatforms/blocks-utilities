using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Accounts
{
    public class ResetPasswordRequest : BaseAccountRequest, IProjectKey
    {
        public bool LogoutFromAllDevices { get; set; }
        public string? ProjectKey { get; set; }
    }

    
}
