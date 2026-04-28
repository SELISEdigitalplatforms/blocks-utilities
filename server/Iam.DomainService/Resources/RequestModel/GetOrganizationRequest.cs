using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class GetOrganizationRequest : IProjectKey
    {
        public string ProjectKey { get ; set ; }
        public string ItemId { get; set ; }
    }
}
