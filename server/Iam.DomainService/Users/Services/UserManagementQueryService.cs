using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Users
{
    public class UserManagementQueryService : IUserManagementQueryService
    {
        private readonly ILogger<UserManagementQueryService> _logger;
        private readonly IUserRepository _userRepository;

        public UserManagementQueryService(
            ILogger<UserManagementQueryService> logger,
            IUserRepository userRepository
        )
        {
            _logger = logger;
            _userRepository = userRepository;
        }

        public async Task<GetAccountsResponse> GetAccountsAsync(GetAccountsRequest query)
        {
            _logger.LogInformation("Accounts get start");

            var (data, count) = await _userRepository.GetUsersAsync<GetAccounts, GetAccountsRequest>(query);

            _logger.LogInformation("Accounts get end");

            return new GetAccountsResponse
            {
                Data = data,
                TotalCount = count
            };
        }

        public async Task<GetAccountResponse> GetAccountAsync()
        {
            _logger.LogInformation("Account get start");

            var bc = BlocksContext.GetContext();
            var user = await _userRepository.GetUserByIdAsync<GetUser>(bc.UserId);

            _logger.LogInformation("Account get end");

            return new GetAccountResponse
            {
                Data = user
            };

        }

        public async Task<bool> IsUserAvailableAsync(IsEmailAvaiableRequest query)
        {
            _logger.LogInformation("User existance search start");

            var user = await _userRepository.GetUserByEmailAsync(query.Email.ToLower());

            _logger.LogInformation("User existance search end");

            return user == null;
        }

        public async Task<GetUsersResponse> GetUsersAsync(GetUsersRequest query)
        {
            _logger.LogInformation("User get start");

            var (data, count) = await _userRepository.GetUsersAsync<GetUser, GetUsersRequest>(query);

            _logger.LogInformation("User get end");

            return new GetUsersResponse
            {
                Data = data,
                TotalCount = count
            };
        }

        public async Task<GetUserResponse> GetUserAsync(string id)
        {
            _logger.LogInformation("User get start");

            var bc = BlocksContext.GetContext();
            var userId = string.IsNullOrWhiteSpace(id) ? bc.UserId : id;
            var user = await _userRepository.GetUserByIdAsync<GetUser>(userId);

            _logger.LogInformation("User get end");

            return new GetUserResponse
            {
                Data = user
            };
        }

        public async Task<GetAccountRolesResponse> GetAccountRolesAsync()
        {
            var bc = BlocksContext.GetContext();
            var roles = await _userRepository.GetRolesBySlugsAsync(bc.UserId);

            return new GetAccountRolesResponse
            {
                Data = roles,
            };
        }

        public async Task<GetAccountPermissionsResponse> GetAccountPermissionsAsync()
        {
            var bc = BlocksContext.GetContext();
            var permissions = await _userRepository.GetPermissionsByResourcesAsync(bc.UserId);
            return new GetAccountPermissionsResponse
            {
                Data = permissions,
            };
        }

        public async Task<GetUserRolesResponse> GetUserRolesAsync(string id)
        {
            var bc = BlocksContext.GetContext();
            var userId = string.IsNullOrWhiteSpace(id) ? bc.UserId : id;
            var roles = await _userRepository.GetRolesBySlugsAsync(userId);

            return new GetUserRolesResponse
            {
                Data = roles,
            };
        }

        public async Task<GetUserPermissionsResponse> GetUserPermissionsAsync(string id)
        {
            var bc = BlocksContext.GetContext();
            var userId = string.IsNullOrWhiteSpace(id) ? bc.UserId : id;
            var permissions = await _userRepository.GetPermissionsByResourcesAsync(userId);
            return new GetUserPermissionsResponse
            {
                Data = permissions,
            };
        }

        public async Task<List<UserTimeline>> GetUserTimelinesAsync(GetUserTimeLineRequest request)
        {
            return await _userRepository.GetUserTimelinesAsync(request);
        }
    }
}
