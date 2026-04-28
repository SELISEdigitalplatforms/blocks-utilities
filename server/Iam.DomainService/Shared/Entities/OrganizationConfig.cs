
using Blocks.Genesis;

namespace Iam.DomainService.Entities
{
    public class OrganizationConfig : BaseEntity
    {
        public bool AllowCreationFromCloud { get; set; }
        public bool AllowCreationFromConstruct { get; set; }
        public bool IsMultiOrgEnabled { get; set; }
        public List<string> Roles { get; set; } = [];
    }
}
