using Iam.DomainService.Resources.ResponseModel;

namespace Iam.DomainService.Resources
{
    public interface IResourceQueryService
    {
        Task<GetPermissionsResponse> GetPermissionsAsync(GetPermissionsRequest query);
        Task<GetPermissionResponse> GetPermissionAsync(string id);
        Task<GetRolesResponse> GetRolesAsync(GetRolesRequest query);
        Task<GetRoleResponse> GetRoleAsync(string id);
        Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync();
        Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync();
    }
}
