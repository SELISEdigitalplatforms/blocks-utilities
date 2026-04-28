using Blocks.Genesis;
using DomainService.Projects;

namespace DomainService.Shared.Entities
{
    public class TenantAsset : BaseEntity
    {
        public string TenantGroupId { get; set; }
        public List<Resource> Resources { get; set; }
    }
}
