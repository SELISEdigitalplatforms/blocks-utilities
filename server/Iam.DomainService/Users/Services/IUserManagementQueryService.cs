using Iam.DomainService.Entities;

namespace Iam.DomainService.Users
{
    public interface IUserManagementQueryService
    {
        Task<bool> IsUserAvailableAsync(IsEmailAvaiableRequest query);
        Task<GetAccountsResponse> GetAccountsAsync(GetAccountsRequest query);
        Task<GetAccountResponse> GetAccountAsync();
        Task<GetUsersResponse> GetUsersAsync(GetUsersRequest query);
        Task<GetUserResponse> GetUserAsync(string id);
        Task<GetAccountRolesResponse> GetAccountRolesAsync();
        Task<GetAccountPermissionsResponse> GetAccountPermissionsAsync();
        Task<GetUserRolesResponse> GetUserRolesAsync(string id);
        Task<GetUserPermissionsResponse> GetUserPermissionsAsync(string id);
        Task<List<UserTimeline>> GetUserTimelinesAsync(GetUserTimeLineRequest request);
    }
}
