using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Users
{
    public class GetUsersRequest : BaseGetsRequest<GetUsersFilter>, IProjectKey
    {
        public string? ProjectKey { get; set; }
    }

    public class GetUsersResponse : BaseQueryListResponse<IQueryable<GetUser>>
    {

    }

}
