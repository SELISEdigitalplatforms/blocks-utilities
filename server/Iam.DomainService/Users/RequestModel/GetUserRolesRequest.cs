using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Users
{
    public class GetUserRolesRequest : IProjectKey
    {
        public string? Id { get; set; }
        public string? ProjectKey { get; set; }
    }

    public class GetUserRolesResponse : BaseQueryListResponse<List<GetUserRole>>
    {

    }
}
