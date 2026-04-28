using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DomainService.OAuth.Services
{
    public class BYOSsoAuthorizationService : SocialAuthorizationServiceBase
    {
        public BYOSsoAuthorizationService(
            ILogger<BYOSsoAuthorizationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationRepository oAuthRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            IUserManagementMutationService userManagementMutationService,
            IConfiguration configuration)
            : base(logger, oAuthJwtAccessTokenManager, oAuthRepository, cacheClient, socialLogInServiceProvider, userManagementMutationService, configuration)
        {
        }

        public override async Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var user = await _oAuthRepository.GetUserByEmailAsync(externalUser.Email);

            return user == null ? await CreateUser(stateInfo, externalUser) : (user, string.Empty);
        }
    }
}