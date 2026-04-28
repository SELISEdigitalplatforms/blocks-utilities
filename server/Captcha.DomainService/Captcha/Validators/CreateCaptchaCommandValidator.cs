using FluentValidation;

namespace Captcha.DomainService.Captcha
{
    public class CreateCaptchaCommandValidator : AbstractValidator<CreateCaptchaRequest>
    {
        public CreateCaptchaCommandValidator()
        {
        }
    }
}
