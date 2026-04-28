namespace Captcha.DomainService.Captcha
{
    public interface ICaptchaGeneratorProvider
    {
        ICaptchaGenerator GetCaptchaGenerator(string configurationName);
        string GetGeneratorName(string configurationName);
    }
}
