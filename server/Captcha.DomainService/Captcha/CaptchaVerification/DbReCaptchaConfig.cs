using Captcha.DomainService.Configuration;

namespace Captcha.DomainService.Captcha
{
    public class DbReCaptchaConfig : IRecaptchaConfig
    {
        private readonly string _verificationUri;
        private readonly string _token;

        public DbReCaptchaConfig(CaptchaConfiguration config, string token)
        {
            _token = token;

            var reCaptchaSecretKey = config.CaptchaSecret;
            _verificationUri = $"https://www.google.com/recaptcha/api/siteverify?secret={reCaptchaSecretKey}&response=" + "{0}";
        }

        public string ResolveRecaptchaUri()
        {
            var requestUri = string.Format(_verificationUri, _token);

            return requestUri;
        }
    }
}
