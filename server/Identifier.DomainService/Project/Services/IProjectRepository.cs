using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Dtos;
using DomainService.Shared.Entities;
using MongoDB.Driver;
using DomainService.Shared;

namespace DomainService.Projects
{
    public interface IProjectRepository
    {
        Task<Tenant> GetByDomainAsync(string name);
        Task<Tenant> GetByIdAsync(string itemId);
        Task InsertProjectAsync(Tenant project);
        Task UpdateProjectAsync(Tenant project);
        Task<List<GroupedProjectsDto>> GetAllByLastModifiedDateAsync(GetProjectsRequest request);
        Task SaveStatusTracerAsync(ProjectStatusTracer statusTrace);
        Task<List<ProjectStatusTracer>> GetAllUnfinishedProjectAsync();
        Task CreateDefaultConfigurationAsync(ProjectStatusTracer statusTrace, Tenant project);
        Task<long> GetProjectCountAsync();
        Task InsertPeopleAsync(ProjectPeople projectPeople);
        Task<bool> SaveTenantCertificate(TenantCertificate tenantCertificate);
        Task<Tenant> GetByTenantIdAsync(string tenantId);
        Task<List<SsoInfo>> GetSsoInfoAsync();
        Task UpdateTenantAssetAsync(TenantAsset asset);
        Task<(TenantAsset assets, long totalCount)> GetTenantAssetAsync(GetAssetRequest request);
        Task SaveTenantAssetAsync(TenantAsset asset);
        Task UpdateRepoResourceAsync(AddAssetRequest request);
        Task SaveRepoInfoAsync(Tenant project, List<Resource>? resources);
        Task UpdateIamConfiguration(Tenant project);
        Task<BlocksGuid> GetBlocksGuidAsync(string tenantGroupId);
        Task<BaseResponse> SaveJWTClaimsAsync(ThirdPartyJWTClaims mapper);
        Task<ThirdPartyJWTClaims> GetThirdPartyJWTClaimsAsync(string itemId);
        Task<bool> IsExistingEnviroment(List<string> enviroments, string tenantGroupId);
        Task<List<Project>> GetSharedProjectsAsync(string? tenantGroupId);
        Task<List<Project>> GetProjectPeoplesAsync(string tenantGroupId);
        Task<List<string>> GetProjectIdsByGroupId(string projectGroupId);
        Task UpdateTenantGroupAsync(UpdateTenantGroupRequest request);
    }
}
