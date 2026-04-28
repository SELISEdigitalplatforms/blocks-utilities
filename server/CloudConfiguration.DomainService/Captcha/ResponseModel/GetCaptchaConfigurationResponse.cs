using Blocks.Genesis;
using CloudConfiguration.DomainService.Captcha.Entities;

namespace CloudConfiguration.DomainService.Captcha.ResponseModel
{
    public class GetCaptchaConfigurationResponse : BaseResponse
    {
        public CaptchaConfiguration Configuration { get; set; }
    }

    public class GetCaptchaConfigurationsResponse
    {
        public List<CaptchaConfiguration> Configurations { get; set; }
    }
}
