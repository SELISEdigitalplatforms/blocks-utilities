using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Users
{
    public interface IUserManagementMutationService
    {
        Task<BaseMutationResponse> CreateUserAsync(CreateUserRequest command);
        Task<BaseMutationResponse> UpdateUserAsync(UpdateUserRequest command);
        Task UpdateUserByLoginInfoAsync(RefreshTokenEvent refreshTokenConsumer);
        Task ExecuteUserMutationCommandAsync(UserMutationEvent command);
        Task<BaseMutationResponse> SaveRolesAndPermissionsAsync(SaveRolesAndPermissionsRequest command);
        Task<bool> CreateUserByEmailAsync(CreateUserByEmailEvent @event);
        Task<BaseMutationResponse> CreateUserViaSsoAsync(CreateUserViaSsoRequest command);
        Task ExecuteUserMutationViaSsoCommandAsync(CreateUserViaSsoEvent command);
        Task<bool> ProcessCreateUserByEmailAfterActionAsync(CreateUserByEmailEvent @event, string userId);
        Task<BaseResponse> DeactivateUserAsync(DeactivateUserRequest request);
    }
}
