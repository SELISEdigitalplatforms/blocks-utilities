using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Captcha.RequestModel
{
    public class GetCaptchaConfigurationRequest : IProjectKey
    {
        public string ProviderName { get; set; }
        public string? ProjectKey { get; set; }
    }
}
