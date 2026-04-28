namespace Captcha.DomainService.Captcha
{
    public interface ICaptchaVerificationServiceProvider
    {
        ICaptchaVerificationService GetCaptchaVerificationService(string provider);
    }
}
