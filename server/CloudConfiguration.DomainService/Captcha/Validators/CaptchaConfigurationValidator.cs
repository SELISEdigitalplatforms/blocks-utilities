using FluentValidation;
using CloudConfiguration.DomainService.Captcha.RequestModel;

namespace CloudConfiguration.DomainService.Captcha.Validators
{
   public class CaptchaConfigurationValidator : AbstractValidator<SaveCaptchaConfigurationRequest>
    {
        public CaptchaConfigurationValidator()
        {
                RuleFor(x => x.Provider)
                .NotEmpty()
                .WithMessage("Provider is required.")
                .Must(provider => provider == null || new[] { "recaptcha", "hcaptcha", "bcaptcha" }.Contains(provider.ToLower()))
                .WithMessage("Provider must be either 'recaptcha', 'hcaptcha', or 'bcaptcha'.");

            When(x => x.Provider != null && x.Provider.ToLower() == "bcaptcha", () =>
            {
                RuleFor(x => x.CaptchaGenerator)
                    .NotEmpty()
                    .WithMessage("CaptchaGenerator is required when Provider is 'bcaptcha'.");
            });

            When(x => x.Provider != null && (x.Provider.ToLower() == "recaptcha" || x.Provider.ToLower() == "hcaptcha"), () =>
            {
                RuleFor(x => x.CaptchaKey)
                    .NotEmpty()
                    .WithMessage("CaptchaKey is required when Provider is 'recaptcha' or 'hcaptcha'.");

                RuleFor(x => x.CaptchaSecret)
                    .NotEmpty()
                    .WithMessage("CaptchaSecret is required when Provider is 'recaptcha' or 'hcaptcha'.");
            });
        }
    }
}
