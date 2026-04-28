namespace Captcha.DomainService.Configuration
{
    public class CaptchaConfigurationService : ICaptchaConfigurationService
    {
        private readonly ICaptchaConfigurationRepository _repository;

        public CaptchaConfigurationService(ICaptchaConfigurationRepository repository)
        {
            _repository = repository;
        }

        public async Task<CaptchaConfiguration> GetByNameAsync(string configurationName)
        {
            return await _repository.GetByProviderAsync(configurationName);
        }

        public async Task<CaptchaConfiguration> GetCaptchaConfigurationAsync()
        {
            return await _repository.GetCaptchaConfigurationAsync();
        }
    }
}
