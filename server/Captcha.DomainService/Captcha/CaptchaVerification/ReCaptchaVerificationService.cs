using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Captcha.DomainService.Utilities;
using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace Captcha.DomainService.Captcha
{
    public class ReCaptchaVerificationService : ICaptchaVerificationService
    {
        private readonly IHttpClientService _httpClientService;
        private readonly ILogger<ReCaptchaVerificationService> _logger;
        private readonly IRecaptchaConfigFactory _recaptchaConfigFactory;

        private readonly string _reCaptchaVerificationUriFormat;

        public ReCaptchaVerificationService(IHttpClientService httpClientService,
                                            IConfiguration configuration,
                                            ILogger<ReCaptchaVerificationService> logger,
                                            IRecaptchaConfigFactory recaptchaConfigFactory)
        {
            _httpClientService = httpClientService;
            _logger = logger;
            _recaptchaConfigFactory = recaptchaConfigFactory;

            var reCaptchaSecretKey = configuration["ReCaptchaSecretKey"];
            _reCaptchaVerificationUriFormat = $"https://www.google.com/recaptcha/api/siteverify?secret={reCaptchaSecretKey}&response=" + "{0}";
        }

        public async Task<VerificationResult> VerifyAsync(string verificationCode)
        {
            _logger.LogInformation("ReCaptchaVerificationHandler: In this method");

            var recaptchaResponse = await VerifyCaptchaAsync(verificationCode);

            _logger.LogInformation("ReCaptchaVerificationHandler: Response - {Response}", JsonConvert.SerializeObject(recaptchaResponse));

            if (recaptchaResponse != null && recaptchaResponse.Success)
            {
                return new VerificationResult
                {
                    Verified = true,
                    HostName = recaptchaResponse.HostName
                };
            }

            return new VerificationResult
            {
                Verified = false,
                Errors = new Dictionary<string, string>
                {
                    { "VerificationCode", "Verification code incorrect" }
                }
            };
        }

        public virtual async Task<RecaptchaResponse> VerifyCaptchaAsync(string token)
        {
            try
            {
                var requestUri = await ResolveVerificationUri(token);
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
                var response = await _httpClientService.SendAsync(httpRequestMessage, "application/x-www-form-urlencoded");

                if (response.IsSuccessStatusCode)
                {
                    var recaptchaResponseJson = await response.Content.ReadAsStringAsync();
                    var recaptchaResponse = JsonConvert.DeserializeObject<RecaptchaResponse>(recaptchaResponseJson);

                    return recaptchaResponse;
                }
                else
                {
                    _logger.LogError("Error in http call for {RequestUri}: Http status code: {StatusCode}", requestUri, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while processing the request.");
            }

            return null;
        }

        public virtual async Task<string> ResolveVerificationUri(string token)
        {
            try
            {
                var config = await _recaptchaConfigFactory.GetRecaptchaConfig(_reCaptchaVerificationUriFormat, token);
                return config.ResolveRecaptchaUri();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred.");
                return string.Format(_reCaptchaVerificationUriFormat, token);
            }
        }
    }
}
