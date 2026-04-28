using Blocks.Genesis;
using CloudConfiguration.DomainService.Authentication.RequestModel;
using CloudConfiguration.DomainService.MFA.RequestModel;
using CloudConfiguration.DomainService.MFA.ResponseModel;
using CloudConfiguration.DomainService.Shared.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.Shared.RequestModel;
using Mfa.DomainService.TOTP;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class MfaController : ControllerBase
    {
        private readonly IMfaManagementService _mfaManagementService;
        private readonly TotpService _totpService;
        private readonly ChangeControllerContext _changeControllerContext;
        private readonly IConfigurationService _configurationService;
        public MfaController(IMfaManagementService mfaManagementService,
                            TotpService totpService,
                            ChangeControllerContext changeControllerContext, IConfigurationService configurationService)
        {
            _changeControllerContext = changeControllerContext;
            _mfaManagementService = mfaManagementService;
            _totpService = totpService;
            _configurationService = configurationService;
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<OtpGenerationResponse> GenerateOTP([FromBody] OtpGenerationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _mfaManagementService.GenerateOTPAsync(request);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<OtpVerificationResponse> VerifyOTP([FromBody] VerifyOtpRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _mfaManagementService.VerifyOTPAsync(request);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseResponse> DisableUserMfa([FromBody] DisableUserMfaRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _mfaManagementService.DisableUserMfa(request);
        }

        [ProtectedEndPoint]
        [HttpGet]
        public async Task<SetUpUserTotpResponse> SetUpTotp([FromQuery] SetUpUserTotpRequest request)
        {
            _changeControllerContext.ChangeContext(request);

            if (string.IsNullOrWhiteSpace(request.UserId))
                return new SetUpUserTotpResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "empty_user_id", "User id should not be empty" } } };

            return await _totpService.GenerateTotpImageByUserAsync(request.UserId);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<OtpGenerationResponse> ResendOtp([FromBody] ResendOtpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.MfaId)) return new OtpGenerationResponse { Errors = new Dictionary<string, string> { { "empty_mfa_id", "Mfa id should not be empty" } } };

            return await _mfaManagementService.ResendOtpAsync(request.MfaId, request.SendPhoneNumberAsEmailDomain);
        }
        #region Cloud Configuration
        [HttpPost]
        [ProtectedEndPoint]
        public async Task<BaseResponse> Save(SaveMfaConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.SaveMfaConfigurationAsync(request);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<GetMfaConfigurationResponse> Get([FromQuery] GetAuthenticationConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.GetMfaConfigurationAsync();
        }
        #endregion
    }
}
