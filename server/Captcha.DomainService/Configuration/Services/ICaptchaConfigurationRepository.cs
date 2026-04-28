namespace Captcha.DomainService.Configuration
{
    public interface ICaptchaConfigurationRepository
    {
        Task<CaptchaConfiguration> GetByProviderAsync(string provider);
        Task<CaptchaConfiguration> GetCaptchaConfigurationAsync();
    }
}
