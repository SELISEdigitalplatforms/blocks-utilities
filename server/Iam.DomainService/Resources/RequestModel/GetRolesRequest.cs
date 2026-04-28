using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Resources
{
    public class GetRolesRequest : BaseGetsRequest<GetRolesFilter>, IProjectKey
    {
        public string ProjectKey { get; set; }
    }
    public class GetRolesResponse : BaseQueryListResponse<IQueryable<Role>>
    {
    }
}
