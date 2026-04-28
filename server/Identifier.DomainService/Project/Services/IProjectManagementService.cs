using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Shared;
using DomainService.Shared.Entities;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DomainService.Projects
{
    public interface IProjectManagementService
    {
        Task<CreateProjectResponse> SaveProjectAsync(CreateProjectRequest project);
        Task ConfigureProjectAsync(Tenant project, ProjectStatusTracer? projectStatus = null);
        Task<List<GroupedProjectsDto>> GetAllAsync(GetProjectsRequest request);
        Task<GetProjectResponse> GetAsync(string projectId);
        Task RestoreUnfinishedProjectAsync();
        Task<RestoreProjectResponse> RestoreProjectAsync(RestoreProjectRequest restoreProjectRequest);
        Task<BaseResponse> UpdateProjectAsync(UpdateProjectRequest request);
        Task<BaseResponse> DisableProjectAsync(string projectId);
        Task<GetAssetResponse> GetAssetAsync(GetAssetRequest request);
        Task<BaseResponse> AddAssetAsync(AddAssetRequest asset);
        Task<BaseResponse> UpdateTokenValidationParametersAsync(UpdateTokenValidationParametersRequest request);
        Task<IActionResult> GetProjectTokenValidationParametersAsync(string projectId);
        Task<SaveThirdPartyJWTClaimsResponse> SaveThirdPartyJWTClaimsAsync(SaveThirdPartyJWTClaimsRequest request);
        Task<ThirdPartyJWTClaims?> GetThirdPartyJWTClaimsAsync(GetThirdPartyJWTClaimsRequest request);
        Task<BaseResponse> UpdateTenantGroupAsync(UpdateTenantGroupRequest request);
    }
}
