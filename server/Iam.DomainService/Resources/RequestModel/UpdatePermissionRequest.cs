using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class UpdatePermissionRequest : PermissionRequestBase, IProjectKey
    {
        public string ItemId { get; set; }
        public bool IsArchived { get; set; }
        public string? ProjectKey { get; set; }
    }

}

