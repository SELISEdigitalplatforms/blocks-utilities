using Blocks.Genesis;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace Captcha.DomainService.Captcha
{
    public class CreateCaptchaRequest
    {
        public required string ConfigurationName { get; set; }
    }

    public class CreateCaptchaRequestResponse : BaseMutationResponse
    {
        public CreateCaptchaRequestResponse(ValidationResult result) : base()
        {
            Errors = result?.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage) ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// command. Id: String representing the Captcha ID.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// command. Captcha: String representing the Captcha.
        /// </summary>
        public string Captcha { get; set; }
    }
}
