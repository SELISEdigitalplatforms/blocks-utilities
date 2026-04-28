

using Blocks.Genesis;

namespace Iam.DomainService.Users
{
    public class DeactivateUserRequest : IProjectKey
    {
        public string UserId { get; set; }
        public string? ProjectKey { get ; set ; }
    }
}
