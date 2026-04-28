using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Resources
{
    public interface IResourceMutationService
    {
        Task<BaseMutationResponse> CreatePermissionAsync(CreatePermissionRequest command);
        Task<BaseMutationResponse> UpdatePermissionAsync(UpdatePermissionRequest command);
        Task<BaseMutationResponse> CreateRoleAsync(CreateRoleRequest command);
        Task<BaseMutationResponse> UpdateRoleAsync(UpdateRoleRequest command);
        Task<SetRolesResponse> SetRolesAsync(SetRolesRequest command);
        Task ExecuteResourceMutationCommandAsync(ResourceMutationEvent command);
        Task<bool> ProcessPermissionAsync(ResourceSetToPermissionMutationEvent command);
        Task<BaseResponse> SaveOrganizationAsync(SaveOrganizationRequest request);
        Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request);
        Task<GetOrganizationResponse> GetOrganizationAsync(GetOrganizationRequest request);
        Task<BaseResponse> SaveganizationConfigAsync(SaveOrganizationConfigRequest request);
        Task<OrganizationConfig> GetOrganizationConfigAsync(GetOrganizationConfigRequest request);
    }
}
