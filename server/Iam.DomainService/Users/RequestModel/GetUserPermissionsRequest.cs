using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Users
{
    public class GetUserPermissionsRequest : IProjectKey
    {
        public string? Id { get; set; }
        public string? ProjectKey { get; set; }
    }

    public class GetUserPermissionsResponse : BaseQueryListResponse<List<GetUserPermission>>
    {

    }
}
