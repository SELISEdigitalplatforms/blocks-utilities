using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.OAuth.Services;
using DomainService.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DomainService.OAuth
{
    public class SocialAuthorizationService : SocialAuthorizationServiceBase
    {
        private readonly IIdentityAccessManagementRepository _repository;
        private readonly IUserRepository _userRepository;

        public SocialAuthorizationService(
            ILogger<SocialAuthorizationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationRepository oAuthRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            IUserManagementMutationService userManagementMutationService,
            IIdentityAccessManagementRepository repository,
            IConfiguration configuration,
            IUserRepository userRepository)
            : base(logger, oAuthJwtAccessTokenManager, oAuthRepository, cacheClient, socialLogInServiceProvider, userManagementMutationService, configuration)
        {
            _repository = repository;
            _userRepository = userRepository;
        }

        protected override void NormalizeExternalUserEmail(IExternalUserData externalUser)
        {
            if (string.IsNullOrWhiteSpace(externalUser.Email) && !string.IsNullOrWhiteSpace(externalUser.UserPrincipalName))
            {
                externalUser.Email = externalUser.UserPrincipalName;
            }
        }

        protected override TokenResponse CreateEmailNotProvidedError()
        {
            return new TokenResponse { Error = "External provider did not provide any email or userPrincipalName.", ErrorDescription = "External provider did not provide any email", StatusCode = 401 };
        }

        protected override TokenResponse CreateUserNotFoundError(string userName)
        {
            return new TokenResponse { Error = "user_not_found", ErrorDescription = $"{userName} is not exit", StatusCode = 401 };
        }

        public override async Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var user = await _oAuthRepository.GetUserByEmailAsync(externalUser.Email);

            if (user == null)
            {
                var signUpSetting = await _repository.GetSignUpSettingAsync();

                if (signUpSetting is not null && signUpSetting.IsSSoSignUpEnabled)
                    return await CreateUser(stateInfo, externalUser);
            }

            user.Department = externalUser.Department;
            user.EmployeeId = externalUser.EmployeeId;
            user.Memberships = [new OrganizationMembership { Roles = externalUser.Roles, OrganizationId = "default" }];
            await _userRepository.UpdateUserAsync(user);

            return (user, string.Empty);
        }
    }
}
