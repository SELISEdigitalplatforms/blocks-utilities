using Blocks.Genesis;
using Blocks.MailDriver;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;

namespace Mfa.DomainService.OTP.Services
{
    public class EmailOtpService : IOtpService
    {
        private readonly ICacheClient _cacheClient;
        private readonly IMfaConfigurationService _configurationService;
        private readonly IMailDriverService _mailDriverService;

        private const int _defaultLifeCycleInSecond = 300;
        private const string _defaultMfaTemplate = "MfaViaEmail";

        public EmailOtpService(ICacheClient cacheClient,
                               IMfaConfigurationService configurationService,
                               IMailDriverService mailDriverService)
        {
            _cacheClient = cacheClient;
            _configurationService = configurationService;
            _mailDriverService = mailDriverService;
        }

        public async Task<OtpGenerationResponse> GenerateAsync(UserInfo userInfo, string? sendPhoneNumberAsEmailDomain = null)
        {
            var context = MfaAuthenticationContext.Create(Guid.NewGuid().ToString(), userInfo.ItemId);
            var code = context.MfaCode;

            await _cacheClient.AddStringValueAsync(context.MfaId, context.Sterilize(), _defaultLifeCycleInSecond);
            var email = userInfo.Email;
            var sendPhoneNumberAsEmail = false;

            if (!string.IsNullOrWhiteSpace(sendPhoneNumberAsEmailDomain))
            {
                if (string.IsNullOrWhiteSpace(userInfo.PhoneNumber))
                    return new OtpGenerationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "phonenumber_not_exist", "PhoneNumber not exist in user for mfa" } } };

                email = $"{userInfo.PhoneNumber.Replace(" ", "").Replace("+", "00")}@{sendPhoneNumberAsEmailDomain}";
                sendPhoneNumberAsEmail = true;
            }

            var result = await SendMfaCodeAsync(email, code, userInfo.Language, sendPhoneNumberAsEmail);

            return new OtpGenerationResponse { MfaId = context.MfaId, IsSuccess = result };
        }

        private async Task<bool> SendMfaCodeAsync(string email, string code, string language, bool sendPhoneNumberAsEmail = false)
        {
            var configuration = await _configurationService.GetAsync();

            var sendMailCommand = new SendMail
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = new Dictionary<string, string>
                                {
                                   { "TwoFactorCode", code }
                                },

                Purpose = !string.IsNullOrWhiteSpace(configuration?.MfaTemplate?.TemplateName) ? configuration.MfaTemplate.TemplateName : _defaultMfaTemplate,
                Language = language ?? "en-US",
                To = [email],
                SendPhoneNumberAsEmail = sendPhoneNumberAsEmail
            };

            var response = await _mailDriverService.SendAsync(sendMailCommand);

            return response.IsSuccess;
        }

        public async Task<OtpVerificationResponse> VerifyAsync(VerifyOtpRequest request)
        {
            var isKeyExist = await _cacheClient.KeyExistsAsync(request.MfaId);

            if (!isKeyExist)
            {
                return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_id" } } };
            }

            var keyValue = await _cacheClient.GetStringValueAsync(request.MfaId);
            var mfaContext = MfaAuthenticationContext.Deserialize(keyValue);

            if (mfaContext.MfaCode == request.VerificationCode)
            {
                await _cacheClient.RemoveKeyAsync(request.MfaId);
                return new OtpVerificationResponse { IsSuccess = true, IsValid = true, UserId = mfaContext.UserId };
            }

            return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_code" } } };
        }
    }
}
