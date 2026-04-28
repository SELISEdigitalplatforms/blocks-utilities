using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Captcha.DomainService.Configuration;
using Captcha.DomainService.Utilities;

namespace Captcha.DomainService.Captcha
{
    public class HCaptchaVerificationService : ICaptchaVerificationService
    {
        private readonly ICaptchaConfigurationService _captchaConfigurationService;
        private readonly ILogger<HCaptchaVerificationService> _logger;
        private readonly IHttpClientService _httpClientService;
        private readonly string _verificationUrl;

        public HCaptchaVerificationService(ICaptchaConfigurationService captchaConfigurationService,
                                           IConfiguration configuration,
                                           ILogger<HCaptchaVerificationService> logger,
                                           IHttpClientService httpClientService)
        {
            _captchaConfigurationService = captchaConfigurationService;
            _logger = logger;
            _httpClientService = httpClientService;
            _verificationUrl = configuration["HCpatchaVerificationUrl"];
        }

        public Task<string> ResolveVerificationUri(string token)
        {
            throw new NotImplementedException();
        }

        public async Task<VerificationResult> VerifyAsync(string verificationCode)
        {
            _logger.LogInformation("HCaptchaVerificationHandler: In this method");

            var hcaptchaResponse = await VerifyCaptchaAsync(verificationCode);

            _logger.LogInformation("HCaptchaVerificationHandler: Response - {Response}", JsonConvert.SerializeObject(hcaptchaResponse));

            if (hcaptchaResponse != null && hcaptchaResponse.Success)
            {
                return new VerificationResult
                {
                    Verified = true,
                    HostName = hcaptchaResponse.HostName
                };
            }

            return new VerificationResult
            {
                Verified = false,
                Errors = new Dictionary<string, string>
                {
                    { "VerificationCode", "Verification failed" }
                }
            };
        }

        public async Task<RecaptchaResponse> VerifyCaptchaAsync(string token)
        {
            try
            {
                CaptchaConfiguration dbConfig = await _captchaConfigurationService.GetCaptchaConfigurationAsync();
                if (dbConfig is null)
                {
                    _logger.LogError("HCaptchaVerificationHandler -> VerifyCaptchaAsync: No config found in db");
                    return new RecaptchaResponse { Success = false, HostName = null };
                }

                var secretKey = dbConfig.CaptchaSecret;
                if (secretKey is null)
                {
                    _logger.LogError("HCaptchaVerificationHandler -> VerifyCaptchaAsync: CaptchaSecret is null in db");
                    return new RecaptchaResponse { Success = false, HostName = null };
                }

                var contentPayloads = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("secret", secretKey),
                    new KeyValuePair<string, string>("response", token)
                };

                var httpRequestMessage = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(_verificationUrl),
                    Content = new FormUrlEncodedContent(contentPayloads)
                };

                var response = await _httpClientService.SendAsync(httpRequestMessage, "application/x-www-form-urlencoded");

                _logger.LogInformation("HCaptchaVerificationHandler -> VerifyCaptchaAsync: {Response}", JsonConvert.SerializeObject(response));

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var verifyResponse = JsonConvert.DeserializeObject<RecaptchaResponse>(responseBody);
                    return verifyResponse;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HCaptchaVerificationHandler -> VerifyCaptchaAsync encountered an error.");
            }

            return new RecaptchaResponse { Success = false, HostName = null };
        }
    }
}
