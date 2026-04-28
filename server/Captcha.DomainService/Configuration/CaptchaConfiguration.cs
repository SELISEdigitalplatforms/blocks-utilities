using Blocks.Genesis;

namespace Captcha.DomainService.Configuration
{
    public class CaptchaConfiguration : BaseEntity
    {
        public string CaptchaKey { get; set; }
        public string CaptchaSecret { get; set; }
        public string Provider { get; set; }
        public string CaptchaGenerator { get; set; }
        public bool IsEnable { get; set; } = true;
    }
}
