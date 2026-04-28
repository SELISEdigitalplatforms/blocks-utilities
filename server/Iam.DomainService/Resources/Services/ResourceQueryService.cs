using Iam.DomainService.Resources.RequestModel;
using Iam.DomainService.Resources.ResponseModel;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Resources
{
    public class ResourceQueryService : IResourceQueryService
    {
        private readonly ILogger<ResourceQueryService> _logger;
        private readonly IResourceRepository _resourceRepository;

        public ResourceQueryService(
            ILogger<ResourceQueryService> logger,
            IResourceRepository resourceRepository
        )
        {
            _logger = logger;
            _resourceRepository = resourceRepository;
        }

        public async Task<GetPermissionResponse> GetPermissionAsync(string id)
        {
            _logger.LogInformation("Permission get start");

            var permission = await _resourceRepository.GetPermissionByIdAsync(id);

            _logger.LogInformation("Permission get end");

            return new GetPermissionResponse
            {
                Data = permission
            };
        }

        public async Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync()
        {
            return await _resourceRepository.GetPermissionsGroupBySeverityAsync();
        }

        public async Task<GetPermissionsResponse> GetPermissionsAsync(GetPermissionsRequest query)
        {
            _logger.LogInformation("Permissions get start");

            var (data, count) = await _resourceRepository.GetPermissionsAsync(query);

            _logger.LogInformation("Permissions get end");

            return new GetPermissionsResponse
            {
                Data = data,
                TotalCount = count
            };
        }

        public async Task<GetRoleResponse> GetRoleAsync(string id)
        {
            _logger.LogInformation("Role get start");

            var role = await _resourceRepository.GetRoleByIdAsync(id);

            _logger.LogInformation("Role get end");

            return new GetRoleResponse
            {
                Data = role
            };
        }

        public async Task<GetRolesResponse> GetRolesAsync(GetRolesRequest query)
        {
            _logger.LogInformation("Roles get start");

            var (data, count) = await _resourceRepository.GetRolesAsync(query);

            _logger.LogInformation("Roles get end");

            return new GetRolesResponse
            {
                Data = data,
                TotalCount = count
            };
        }

        public async Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync()
        {
            return (await _resourceRepository.GetResourceGroupsAsync());
        }
    }
}
