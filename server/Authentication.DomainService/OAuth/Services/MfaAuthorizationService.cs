using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;

namespace DomainService.OAuth.Services
{
    public class MfaAuthorizationService : ITokenService
    {
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IOtpServiceFactory _tpServiceFactory;
        private readonly IAuthenticationRepository _oAuthRepository;

        public MfaAuthorizationService(IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
                                       IOtpServiceFactory tpServiceFactory,
                                       IAuthenticationRepository oAuthRepository)
        {
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _tpServiceFactory = tpServiceFactory;
            _oAuthRepository = oAuthRepository;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            var otpService = _tpServiceFactory.GetOTPService(request.MfaType);
            var response = await otpService.VerifyAsync(new VerifyOtpRequest { AuthType = request.MfaType, MfaId = request.MfaId, VerificationCode = request.Code });

            if (response.IsValid)
            {
                user = await _oAuthRepository.GetUserByIdAsync(response.UserId);
                return !user.IsMfaVerified ? new TokenResponse { Error = "unverified_user_mfa", ErrorDescription = "Unverified user mfa please verify the mfa first", StatusCode = 400 } : await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);
            }

            return new TokenResponse { Error = "invalid_mfa_code", ErrorDescription = "Mfa code is not valid", StatusCode = 401 };
        }
    }
}
