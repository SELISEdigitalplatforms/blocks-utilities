using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Resources
{
    public class GetOrganizationsRequest : BaseGetsRequest<Organization>, IProjectKey
    {
        public string ProjectKey { get ; set ; }
    }
}
