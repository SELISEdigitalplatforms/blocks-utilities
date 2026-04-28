using Blocks.Genesis;

namespace Captcha.DomainService.Configuration
{
    public interface ICaptchaConfigurationService
    {
        // Returns first or default config
        Task<CaptchaConfiguration> GetCaptchaConfigurationAsync();
        Task<CaptchaConfiguration> GetByNameAsync(string configurationName);
    }
}
