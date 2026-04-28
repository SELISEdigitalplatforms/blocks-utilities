namespace Captcha.DomainService.Captcha;

public interface ICaptchaProcessor
{
    public CaptchaInformation GetCaptchaInformation(string provider);
    public Task<string> SubmitAndCreateVerificationCodeAsync(string captchaId);
    public Task<VerificationResult> VerifyCaptchaAsync(string configProvider, string verificationCode);
}
