using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Users
{
    public class GetUserRequest : IProjectKey
    {
        public string? Id { get; set; }
        public string? ProjectKey { get; set; }
    }

    public class GetUserResponse : BaseQueryResponse<GetUser>
    {
    }
}
