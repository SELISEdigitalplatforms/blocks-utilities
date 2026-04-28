using Blocks.Genesis;

namespace Mfa.DomainService.Shared
{
    public class DisableUserMfaRequest : IProjectKey
    {
        public string UserId { get; set; }
        public string? ProjectKey { get; set; }
    }
}
