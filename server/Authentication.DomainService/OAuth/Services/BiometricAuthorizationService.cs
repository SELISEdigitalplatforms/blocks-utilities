using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using Iam.DomainService.Entities;

namespace DomainService.OAuth.Services
{
    public class BiometricAuthorizationService : ITokenService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;

        public BiometricAuthorizationService(IAuthenticationRepository authenticationRepository, 
                                      IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager)
        {
            _authenticationRepository = authenticationRepository;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            var client = await _authenticationRepository.AuthenticateBiometricCredentialAsync(request.BiometricId, request.BiometricKey);

            if (client == null)
            {
                return new TokenResponse { Error = "invalid_client", ErrorDescription = "The biometricId or biometricKey is not valid" };
            }

            user = await _authenticationRepository.GetUserByIdAsync(client.UserId);

            if (user == null || !user.Active) return new TokenResponse { Error = "", ErrorDescription = "The biometricId or biometricKey is not valid" };

            return await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);
        }
    }
}
