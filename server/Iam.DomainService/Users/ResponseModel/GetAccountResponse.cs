using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Users
{
    public class GetAccountResponse : BaseQueryResponse<GetUser>
    {
        public List<GetUserPermission> Permissions { get; set; }
    }
}
