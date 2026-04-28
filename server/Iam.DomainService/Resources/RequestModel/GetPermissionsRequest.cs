using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Resources
{
    public class GetPermissionsRequest : BaseGetsRequest<GetPermissionFilter>, IProjectKey
    {
        public List<string> Roles { get; set; } = [];
        public string? ProjectKey { get; set; }
    }

    public class GetPermissionsResponse : BaseQueryListResponse<IQueryable<Permission>>
    {
    }
}
