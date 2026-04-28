using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class SaveOrganizationRequest : IProjectKey
    {
        public string ProjectKey { get ; set ; }
        public string Name { get ; set ; }
        public string? ItemId { get ; set ; }
        public bool IsEnable { get ; set ; }
    }
}
