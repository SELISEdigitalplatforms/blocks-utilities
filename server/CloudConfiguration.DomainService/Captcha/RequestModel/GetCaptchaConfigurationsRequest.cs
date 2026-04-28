using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Captcha.RequestModel
{
    public class GetCaptchaConfigurationsRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
    }
}
