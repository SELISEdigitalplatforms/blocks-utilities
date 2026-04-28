
using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Captcha.RequestModel
{
    public class UpdateCaptchaConfigurationStatusRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public bool IsEnable { get; set; }
        public string ProjectKey { get; set; }
    }
}
