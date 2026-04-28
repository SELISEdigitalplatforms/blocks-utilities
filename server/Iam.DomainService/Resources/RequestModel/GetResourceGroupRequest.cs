using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class GetResourceGroupRequest : IProjectKey
    {
        public string ProjectKey { get ; set ; }
    }
    public class GetResourceGroupResponse
    { 
        public string ResourceGroup { get; set; }
        public int Count { get; set; }
    }
}
