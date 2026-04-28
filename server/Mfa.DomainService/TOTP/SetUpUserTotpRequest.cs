using Blocks.Genesis;

namespace Mfa.DomainService.TOTP
{
    public class SetUpUserTotpRequest : IProjectKey
    {
        public string ProjectKey { get; set; }
        public string UserId { get; set; }
    }
}
