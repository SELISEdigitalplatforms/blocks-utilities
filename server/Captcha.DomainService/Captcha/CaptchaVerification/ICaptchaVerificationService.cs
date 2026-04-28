namespace Captcha.DomainService.Captcha
{
    public interface ICaptchaVerificationService
    {
        Task<VerificationResult> VerifyAsync(string verificationCode);
        Task<RecaptchaResponse> VerifyCaptchaAsync(string token);
        Task<string> ResolveVerificationUri(string token);
    }
}
