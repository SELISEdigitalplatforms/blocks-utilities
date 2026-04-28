using Blocks.Genesis;

namespace Iam.DomainService.Shared.Entities
{
    public class Organization : BaseEntity
    {
        public string Name { get; set; }
        public bool IsEnable { get; set; } = true;
    }
}
