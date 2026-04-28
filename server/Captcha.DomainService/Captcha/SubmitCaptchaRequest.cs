using Blocks.Genesis;
using FluentValidation.Results;

namespace Captcha.DomainService.Captcha
{
    public class SubmitCaptchaRequest
    {
        /// <summary>
        /// command. Id: String representing the Captcha ID.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// command. Value: String representing the value of the Captcha.
        /// </summary>
        public string Value { get; set; }
    }

    public class SubmitCaptchaRequestResponse : BaseMutationResponse
    {
        public SubmitCaptchaRequestResponse(ValidationResult result) : base()
        {
            Errors = result?.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage) ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// command. Verification Code: String representing the verification code for Captcha response.
        /// </summary>
        public string VerificationCode { get; set; }
    }
}
