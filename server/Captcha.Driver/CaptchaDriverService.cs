using Blocks.CaptchaDriver;
using Captcha.DomainService.Captcha;

namespace Blocks.CaptchaDriver
{
    public class CaptchaDriverService : ICaptchaDriverService
    {
        private readonly ICaptchaService _captchaService;

        public CaptchaDriverService(ICaptchaService captchaService)
        {
            _captchaService = captchaService;
        }

        public CreateCaptchaRequestResponse Create(CreateCaptchaRequest command)
        {
            return _captchaService.CreateCaptcha(command);
        }

        public async Task<SubmitCaptchaRequestResponse> Submit(SubmitCaptchaRequest command)
        {
            return await _captchaService.SubmitCaptchaAsync(command);
        }

        public async Task<VerifyCaptchaRequestResponse> Verify(VerifyCaptchaRequest query)
        {
            return await _captchaService.VerifyCaptchaAsync(query);
        }
    }
}
