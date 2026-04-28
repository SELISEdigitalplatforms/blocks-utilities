using Captcha.DomainService.Captcha;

namespace Blocks.CaptchaDriver
{
    /// <summary>
    /// Service for handling CAPTCHA operations including creation, submission, and verification.
    /// </summary>
    public interface ICaptchaDriverService
    {
        /// <summary>
        /// Creates a new CAPTCHA based on the provided request.
        /// </summary>
        /// <param name="command">The request containing the ConfigurationName for the CAPTCHA.</param>
        /// <returns>A response containing the Captcha Id, Captcha and IsValid status.</returns>
        CreateCaptchaRequestResponse Create(CreateCaptchaRequest command);
        /// <summary>
        /// Submits a CAPTCHA and generates a verification code.
        /// </summary>
        /// <param name="command">The request containing the CAPTCHA Id and Value to be submitted.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the response with the VerificationCode and IsValid status.</returns>
        Task<SubmitCaptchaRequestResponse> Submit(SubmitCaptchaRequest command);
        /// <summary>
        /// Verifies a CAPTCHA using the provided verification code and configuration name.
        /// </summary>
        /// <param name="query">The request containing the VerificationCode and ConfigurationName.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the response with the VerificationResult and IsValid status.</returns>
        Task<VerifyCaptchaRequestResponse> Verify(VerifyCaptchaRequest query);
    }
}
