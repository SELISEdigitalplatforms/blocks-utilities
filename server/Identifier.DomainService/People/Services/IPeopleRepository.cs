using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Shared.Entities;
using Iam.DomainService.Entities;

namespace DomainService.People
{
    public interface IPeopleRepository
    {
        Task<bool> InsertPeoplesAsync(List<ProjectPeople> projectPeoples);
        Task<bool> RemovePeoplesAsync(string email, List<string> projectKeys);
        Task<(List<GetProjectPeople> peoples, long totalCount, long peoplesTotalCount, bool isOwner)> GetPeoplesAsync(GetPeoplesRequest request);
        Task<List<ProjectPeople>> GetProjectPeoplesAsync(string userId, List<string> projectKeys);
        Task<ProjectPeople> GetProjectPeopleAsync(string id);
        Task<List<User>> GetUsersByEmailAsync(List<string> emails);
        Task<Tenant> GetProjectByIdAsync(string projectKey);
        Task<User> GetUserByIdAsync(string userId);
        Task<bool> UpdateProjectPeoples(List<string> ids);
        Task<bool> IsPeoplesWithinLimit(InvitationDetails request, string resource);
        Task<bool> IsOwner(string email, List<string> projectKeys);
        Task<bool> UpdateProjectPeopleOwnerShipAsync(List<string> ids, bool ownerShipStatus);
        Task<ProjectPeople> GetProjectPeopleByTenantIdAndUserIdAsync(string tenantId, string userId);
        Task<bool> UpdateProjectOwnerShipAsync(List<string> tenantIds, string userId);
        Task<SignUpSetting> GetSignUpSettingAsync();
    }
}
