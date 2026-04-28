namespace Captcha.DomainService.Captcha
{
    public class LocalReCaptchaConfig : IRecaptchaConfig
    {
        private readonly string _verificationUri;
        private readonly string _token;

        public LocalReCaptchaConfig(string vertificationUri, string token)
        {
            _verificationUri = vertificationUri;
            _token = token;
        }

        public string ResolveRecaptchaUri()
        {
            var requestUri = string.Format(_verificationUri, _token);

            return requestUri;
        }
    }
}
