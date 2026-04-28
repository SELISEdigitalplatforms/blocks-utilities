using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using Iam.DomainService.Entities;
using System.Text.Json;

namespace DomainService.OAuth.Services
{
    public class AuthorizeCodeService : ITokenService
    {
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly ICacheClient _cacheClient;

        public AuthorizeCodeService(IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
                                        IAuthenticationRepository oAuthRepository,
                                        ICacheClient cacheClient)
        {
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _oAuthRepository = oAuthRepository;
            _cacheClient = cacheClient;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            var stateCacheData = await _cacheClient.GetStringValueAsync(request.Code);
            var stateInfo = stateCacheData != null? JsonSerializer.Deserialize<StateInfo>(stateCacheData) : null;

            if (string.IsNullOrWhiteSpace(stateInfo?.UserName))
            {
                return new TokenResponse { Error = "invalid_code", ErrorDescription = "The code is either not valid or expire" };
            }

            request.Scope = stateInfo?.Scope ?? "";
            await _cacheClient.RemoveKeyAsync(request.Code); 
            user = await _oAuthRepository.GetUserByEmailAsync(stateInfo.UserName);

            if (!IsValidUser(user)) return OAuthError.InValidResponse(request);
            if (!IsUserActiveAndVerified(user)) return OAuthError.UserNotActiveOrVerifiedResponse();

            return await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user, stateInfo: stateInfo);
        }

        private static bool IsValidUser(User user) =>
           user != null;

        private static bool IsUserActiveAndVerified(User user) =>
            user.Active && user.IsVarified;
    }
}
