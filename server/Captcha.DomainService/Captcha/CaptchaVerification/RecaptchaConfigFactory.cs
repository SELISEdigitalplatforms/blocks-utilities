using Microsoft.Extensions.Logging;
using Captcha.DomainService.Configuration;

namespace Captcha.DomainService.Captcha
{
    public class RecaptchaConfigFactory : IRecaptchaConfigFactory
    {
        private readonly ILogger<RecaptchaConfigFactory> _logger;
        private readonly ICaptchaConfigurationService _captchaConfigurationService;

        public RecaptchaConfigFactory(ILogger<RecaptchaConfigFactory> logger,
                                      ICaptchaConfigurationService captchaConfigurationService)
        {
            _logger = logger;
            _captchaConfigurationService = captchaConfigurationService;
        }

        public async Task<IRecaptchaConfig> GetRecaptchaConfig(string reCaptchaVerificationUriFormat, string token)
        {
            try
            {
                CaptchaConfiguration config = await GetConfigFromDb();

                if (config == null)
                {
                    _logger.LogInformation("No custom config found. Going with local config");

                    return new LocalReCaptchaConfig(
                        vertificationUri: reCaptchaVerificationUriFormat,
                        token: token);
                }

                return new DbReCaptchaConfig(
                    config: config,
                    token: token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred.");

                return new LocalReCaptchaConfig(
                    vertificationUri: reCaptchaVerificationUriFormat,
                    token: token);
            }
        }

        public async Task<CaptchaConfiguration> GetConfigFromDb()
        {
            return await _captchaConfigurationService.GetCaptchaConfigurationAsync();
        }
    }
}
