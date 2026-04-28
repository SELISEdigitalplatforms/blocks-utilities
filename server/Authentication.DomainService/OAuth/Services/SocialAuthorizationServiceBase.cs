using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DomainService.OAuth.Services
{
    public abstract class SocialAuthorizationServiceBase : ITokenService
    {
        protected readonly ILogger _logger;
        protected readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        protected readonly IAuthenticationRepository _oAuthRepository;
        protected readonly ICacheClient _cacheClient;
        protected readonly ISocialLogInServiceProvider _socialLogInServiceProvider;
        protected readonly IUserManagementMutationService _userManagementMutationService;
        private readonly IConfiguration _configuration;

        protected SocialAuthorizationServiceBase(
            ILogger logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationRepository oAuthRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            IUserManagementMutationService userManagementMutationService,
            IConfiguration configuration)
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _oAuthRepository = oAuthRepository;
            _cacheClient = cacheClient;
            _socialLogInServiceProvider = socialLogInServiceProvider;
            _userManagementMutationService = userManagementMutationService;
            _configuration = configuration;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            _logger.LogInformation("Social Authentication start");

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                _logger.LogError("Code is required");
                return new TokenResponse { Error = "code_require", ErrorDescription = "code_require", StatusCode = 400 };
            }

            if (string.IsNullOrWhiteSpace(request.State))
            {
                return new TokenResponse { Error = "state_require", ErrorDescription = "state_require", StatusCode = 400 };
            }

            var stateCacheData = await _cacheClient.GetStringValueAsync(request.State);

            if (string.IsNullOrWhiteSpace(stateCacheData))
            {
                _logger.LogError("State data not found");
                return new TokenResponse { Error = "state_data_not_found", ErrorDescription = "state_data_not_found", StatusCode = 400 };
            }

            var stateInfo = JsonSerializer.Deserialize<StateInfo>(stateCacheData);
            if (stateInfo == null)
            {
                _logger.LogError("State data is invalid");
                return new TokenResponse { Error = "state_data_invalid", ErrorDescription = "state_data_invalid", StatusCode = 400 };
            }

            stateInfo.Code = request.Code;

            var externalUser = await _socialLogInServiceProvider.HandleSocialLogin(stateInfo);
            await _cacheClient.RemoveKeyAsync(request.State);

            NormalizeExternalUserEmail(externalUser);

            if (string.IsNullOrWhiteSpace(externalUser.Email))
            {
                return CreateEmailNotProvidedError();
            }

            if (string.IsNullOrWhiteSpace(externalUser.ExternalProviderUserId))
            {
                return new TokenResponse { Error = "External provider did not provide any user id.", ErrorDescription = "External provider did not provide any user id", StatusCode = 401 };
            }

            (user, string redirectUri) = await GetUser(stateInfo, externalUser);

            if (user == null)
            {
                if(!string.IsNullOrWhiteSpace(redirectUri))
                {
                    return new TokenResponse { SsoUserRedirectUrl = redirectUri };
                }
               
                return CreateUserNotFoundError(externalUser.Email?? "");
            }

            if (!user.Active || !user.IsVarified)
            {
                return new TokenResponse { Error = "There is a user with external user id but is not active.", ErrorDescription = "There is a user with external user id but is not active", StatusCode = 401 };
            }

            return await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);
        }

        protected virtual void NormalizeExternalUserEmail(IExternalUserData externalUser)
        {
            // Default implementation does nothing
            // Override in derived classes if needed
        }

        protected virtual TokenResponse CreateEmailNotProvidedError()
        {
            return new TokenResponse { Error = "External provider did not provide any email.", ErrorDescription = "External provider did not provide any email", StatusCode = 401 };
        }

        protected virtual TokenResponse CreateUserNotFoundError(string userName)
        {
            return new TokenResponse { Error = "Failed to create user", ErrorDescription = "Failed to create user", StatusCode = 401 };
        }

        public abstract Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser);

        public async Task<(User? user, string redirectUrl)> CreateUser(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var blocksContext = BlocksContext.GetContext();

            var userPayload = new CreateUserViaSsoRequest
            {
                Email = externalUser.Email,
                ExternalUserId = externalUser.ExternalProviderUserId,
                FirstName = externalUser.FirstName,
                LastName = externalUser.LastName,
                PhoneNumber = externalUser.PhoneNumber,
                IsVarified = true,
                Active = true,
                MailPurpose = "AccountActivated",
                SendWelcomeMail = true,
                Platform = stateInfo.Provider,
                ProfileImageUrl = externalUser.ProfileImageUrl,
                Memberships = [new OrganizationMembership { Roles = externalUser.Roles, OrganizationId = "default" }],
                Permissions = externalUser.Permissions ?? [],
                ProjectKey = blocksContext.TenantId,
                DepartMent = externalUser.Department,
                EmployeeId = externalUser.EmployeeId
            };

            var code = Guid.NewGuid().ToString("n");
            await _cacheClient.AddStringValueAsync(code, JsonSerializer.Serialize(userPayload), 5000);
            var redirectUrl = $"{_configuration["SsoSignUpUri"]}?code={code}&username={externalUser.Email}&firstname={externalUser.FirstName}&lastname={externalUser.LastName}";
            return (null, redirectUrl);
        }
    }
}
