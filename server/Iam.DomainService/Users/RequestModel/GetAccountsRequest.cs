using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Users
{
    public class GetAccountsRequest : BaseGetsRequest<GetUsersFilter>
    {

    }

    public class GetAccountsResponse : BaseQueryListResponse<IQueryable<GetAccounts>>
    {
        
    }
}
