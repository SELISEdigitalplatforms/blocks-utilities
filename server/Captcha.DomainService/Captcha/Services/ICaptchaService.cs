namespace Captcha.DomainService.Captcha
{
    public interface ICaptchaService
    {
        CreateCaptchaRequestResponse CreateCaptcha(CreateCaptchaRequest command);
        Task<SubmitCaptchaRequestResponse> SubmitCaptchaAsync(SubmitCaptchaRequest command);
        Task<VerifyCaptchaRequestResponse> VerifyCaptchaAsync(VerifyCaptchaRequest query);
    }
}
