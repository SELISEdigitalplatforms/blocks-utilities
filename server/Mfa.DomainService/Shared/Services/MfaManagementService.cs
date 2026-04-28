using Blocks.Genesis;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Iam.DomainService.Entities;
using Mfa.DomainService.Shared;

namespace Mfa.DomainService.Services
{
    public class MfaManagementService : IMfaManagementService
    {
        private readonly IOtpServiceFactory _otpServiceFactory;
        private readonly IMfaManagementRepository _mfaRepository;
        private readonly IMfaConfigurationService _configurationService;
        private readonly ICacheClient _cacheClient;

        public MfaManagementService(IOtpServiceFactory otpServiceFactory,
                                    IMfaManagementRepository mdmRepository,
                                    IMfaConfigurationService configurationService,
                                    ICacheClient cacheClient)
        {
            _otpServiceFactory = otpServiceFactory;
            _mfaRepository = mdmRepository;
            _configurationService = configurationService;
            _cacheClient = cacheClient;
        }

        public async Task<OtpGenerationResponse> GenerateOTPAsync(OtpGenerationRequest request)
        {
            var configuration = await _configurationService.GetAsync();
            var isConfigurationExist = configuration?.EnableMfa ?? false;

            if (!isConfigurationExist)
            {
                return new OtpGenerationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "mfa_not_enable", "Please enable mfa for your application first" } } };
            }

            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return new OtpGenerationResponse { Errors = new Dictionary<string, string> { { "empty_user_id", "Mfa is not enable for this user" } } };
            }

            var userInfo = await _mfaRepository.GetItemAsync<UserInfo>(u => u.ItemId == request.UserId, "Users");

            var otpService = _otpServiceFactory.GetOTPService(request?.MfaType ?? userInfo.UserMfaType);
            return await otpService.GenerateAsync(userInfo, request?.SendPhoneNumberAsEmailDomain ?? "");
        }

        public async Task<OtpVerificationResponse> VerifyOTPAsync(VerifyOtpRequest request)
        {
            var otpService = _otpServiceFactory.GetOTPService(request.AuthType);
            var verificationResponse = await otpService.VerifyAsync(request);

            if (verificationResponse.IsValid && !request.IsFromTokenCall) 
            {
                var updates = new Dictionary<string, object>
                          {
                             { nameof(UserMfaInfo.MfaEnabled), true },
                             { nameof(UserMfaInfo.UserMfaType), request.AuthType },
                             { nameof(UserMfaInfo.IsMfaVerified), true }
                          };

                await _mfaRepository.UpdatePartialAsync<UserMfaInfo>(verificationResponse.UserId, updates, "Users");
            }
            
            return verificationResponse;
        }

        public async Task<BaseResponse> DisableUserMfa(DisableUserMfaRequest request)
        {
            if(string.IsNullOrWhiteSpace(request.UserId))
            {
                return new BaseResponse { Errors = new Dictionary<string, string> { { "empty_user_id", "User id should not be empty" } } };
            }

            if(request.UserId != BlocksContext.GetContext()?.UserId)
            {
                return new BaseResponse { Errors = new Dictionary<string, string> { { "invalid_user_id", "Yor are not allowed to disable mfa" } } };
            }

            var updates = new Dictionary<string, object>
                          {
                             { nameof(UserMfaInfo.MfaEnabled), false },
                             { nameof(UserMfaInfo.UserMfaType), UserMfaType.None },
                             { nameof(UserMfaInfo.IsMfaVerified), false }
                          };

            await _mfaRepository.UpdatePartialAsync<UserMfaInfo>(request.UserId, updates, "Users");

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<OtpGenerationResponse> ResendOtpAsync(string mfaId, string sendPhoneNumberAsEmailDomain)
        {
            var isKeyExist = await _cacheClient.KeyExistsAsync(mfaId);

            if (!isKeyExist)
            {
                return new OtpGenerationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_id" } } };
            }

            var keyValue = await _cacheClient.GetStringValueAsync(mfaId);
            var mfaContext = MfaAuthenticationContext.Deserialize(keyValue);

            return await GenerateOTPAsync(new OtpGenerationRequest { UserId = mfaContext.UserId, MfaType = UserMfaType.Email, SendPhoneNumberAsEmailDomain = sendPhoneNumberAsEmailDomain });
        }
    }
}
