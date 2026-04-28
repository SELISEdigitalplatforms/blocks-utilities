using Blocks.Genesis;
using Iam.DomainService.Users;

namespace IamDriver
{
    public interface IIamDriverService
    {
        Task<BaseMutationResponse> CreateUser(CreateUserRequest createUserRequest);
        Task<BaseMutationResponse> CreateUserViaSso(CreateUserViaSsoRequest command);
    }
}
