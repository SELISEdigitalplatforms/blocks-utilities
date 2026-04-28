using Blocks.Genesis;
using Iam.DomainService.Users;

namespace IamDriver
{
    /// <summary>
    /// Provides IAM (Identity and Access Management) services for user management.
    /// </summary>
 
    public class IamDriverService : IIamDriverService
    {
        private readonly IUserManagementMutationService _userManagementMutationService;

        public IamDriverService(IUserManagementMutationService userManagementMutationService)
        {
            _userManagementMutationService = userManagementMutationService;
        }

        /// <summary>
        /// Creates a new user in the IAM system.
        /// </summary>
        /// <param name="createUserRequest">The request object containing user details.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="BaseMutationResponse"/> indicating the success or failure of the operation.</returns>
        
        public async Task<BaseMutationResponse> CreateUser(CreateUserRequest createUserRequest)
        {
            return await _userManagementMutationService.CreateUserAsync(createUserRequest);
        }

        /// <summary>
        /// Creates a new user in the IAM system via SSO.
        /// </summary>
        /// <param name="CreateUserViaSsoRequest">The request object containing user details.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="BaseMutationResponse"/> indicating the success or failure of the operation.</returns>
        public async Task<BaseMutationResponse> CreateUserViaSso(CreateUserViaSsoRequest command)
        {
            return await _userManagementMutationService.CreateUserViaSsoAsync(command);
        }
    }
}
