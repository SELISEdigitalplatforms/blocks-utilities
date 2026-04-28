using Microsoft.Extensions.DependencyInjection;

namespace Captcha.DomainService.Captcha
{
    public class CaptchaVerificationServiceProvider : ICaptchaVerificationServiceProvider
    {
        private readonly IServiceProvider _serviceProvider;

        public CaptchaVerificationServiceProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ICaptchaVerificationService GetCaptchaVerificationService(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                provider = "bcaptcha";
            }

            if (provider.Equals("bcaptcha", StringComparison.InvariantCultureIgnoreCase))
            {
                return _serviceProvider.GetService<BlocksCaptchaVerificationService>();
            }

            if (provider.Equals("recaptcha", StringComparison.InvariantCultureIgnoreCase))
            {
                return _serviceProvider.GetService<ReCaptchaVerificationService>();
            }

            if (provider.Equals("hcaptcha", StringComparison.InvariantCultureIgnoreCase))
            {
                return _serviceProvider.GetService<HCaptchaVerificationService>();
            }

            return default;
        }

    }
}
