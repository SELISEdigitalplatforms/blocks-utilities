using Captcha.DomainService.Configuration;

namespace Captcha.DomainService.Captcha
{
    public interface IRecaptchaConfigFactory
    {
        public Task<IRecaptchaConfig> GetRecaptchaConfig(
               string reCaptchaVerificationUriFormat,
               string token);

        public Task<CaptchaConfiguration> GetConfigFromDb();
    }
}
