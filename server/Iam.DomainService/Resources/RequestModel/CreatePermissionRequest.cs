using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class CreatePermissionRequest : PermissionRequestBase, IProjectKey
    {
        public string? ProjectKey { get; set; }
    }
}
