namespace Captcha.DomainService.Utilities
{
    public class ContextCaptchaIdGeneratorService : IContextCaptchaIdGeneratorService
    {
        public string GetContextCaptchaId()
        {
            return Guid.NewGuid().ToString("n");
        }
    }
}
