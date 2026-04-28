using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Captcha.RequestModel
{
    public class SaveCaptchaConfigurationRequest : IProjectKey
    {
        public string CaptchaKey { get; set; }
        public string CaptchaSecret { get; set; }
        public string Provider { get; set; }
        public string CaptchaGenerator { get; set; }
        public bool IsEnable { get; set; }
        public string? ProjectKey { get; set; }
    }
}
