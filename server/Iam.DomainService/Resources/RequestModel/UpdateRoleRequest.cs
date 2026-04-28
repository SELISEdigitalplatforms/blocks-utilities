using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class UpdateRoleRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public string? ProjectKey { get; set; }
    }
}
