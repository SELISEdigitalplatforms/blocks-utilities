using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class CreateRoleRequest : IProjectKey
    {
        public string Name { get; set; }
        public string? Description { get; set; }
        public string Slug { get; set; }
        public string? ProjectKey { get; set; }
    }
}
