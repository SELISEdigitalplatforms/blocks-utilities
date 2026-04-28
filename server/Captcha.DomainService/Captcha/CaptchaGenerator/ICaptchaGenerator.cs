namespace Captcha.DomainService.Captcha
{
    public interface ICaptchaGenerator
    {
        string Generate(string captchaString);
    }
}
