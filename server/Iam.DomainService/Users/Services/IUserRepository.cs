using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Users
{
    public interface IUserRepository
    {
        Task<User> GetUserByUserNameOrgIdAsync(string userName, string organizatoinId = "");
        Task<bool> CheckPasswordBlackListedAsync(string password, string tenantId);
        Task<User> GetUserByEmailAsync(string email);
        Task<bool> CreateUserAsync(User user);
        Task<User> GetUserByIdAsync(string itemId);
        Task<T> GetUserByIdAsync<T>(string itemId);
        Task<bool> UpdateUserAsync(User user);
        Task<(IQueryable<T>?, long)> GetUsersAsync<T, R>(R query) where R : BaseGetsRequest<GetUsersFilter>;
        Task<IamConfiguration> GetIamConfigurationAsync();
        Task<bool> InsertUserKeyMapAsync(UserKeyMap userKeyMap);
        Task<bool> InsertUserTimelineAsync(UserTimeline userTimeline);
        Task<List<GetUserPermission>> GetPermissionsByResourcesAsync(string id);
        Task<List<GetUserPermission>> GetPermissionsByResourcesAsync(List<string> permissions);
        Task<List<GetUserRole>> GetRolesBySlugsAsync(string id);
        Task<List<GetUserRole>> GetRolesBySlugsAsync(List<string> roles);
        Task<List<UserTimeline>> GetUserTimelinesAsync(GetUserTimeLineRequest request);
        Task<string> GetProjectIdFromProjectPeopleAsync(string userId);
    }
}
