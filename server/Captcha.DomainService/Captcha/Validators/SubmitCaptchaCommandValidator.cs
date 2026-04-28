using Blocks.Genesis;
using FluentValidation;
using FluentValidation.Results;

namespace Captcha.DomainService.Captcha
{
    public class SubmitCaptchaCommandValidator : AbstractValidator<SubmitCaptchaRequest>,
        ISubmitCaptchaCommandValidator
    {
        private readonly ICacheClient _cache;

        public SubmitCaptchaCommandValidator(ICacheClient cache)
        {
            _cache = cache;

            RuleFor(c => c.Id)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .NotEmpty();

            RuleFor(c => c.Value)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Value can not be null or empty")
                .MustAsync(BeMatchedWithExistingAsync)
                .When(command => !string.IsNullOrWhiteSpace(command.Id))
                .WithMessage("Value did not match.");
        }

        public async virtual Task<bool> BeMatchedWithExistingAsync(SubmitCaptchaRequest submitCaptchaCommand,
                                                                   string captchaValue,
                                                                   CancellationToken cancellationToken)
        {
            var storedCaptchaValue = await _cache.GetStringValueAsync(submitCaptchaCommand.Id);
            var valueMatched = captchaValue.Equals(storedCaptchaValue, StringComparison.InvariantCultureIgnoreCase);

            return valueMatched;
        }

        public virtual Task<ValidationResult> ValidateAsync(SubmitCaptchaRequest command)
        {
            return base.ValidateAsync(command);
        }
    }

    public interface ISubmitCaptchaCommandValidator
    {
        Task<bool> BeMatchedWithExistingAsync(
            SubmitCaptchaRequest submitCaptchaCommand,
            string captchaValue,
            CancellationToken cancellationToken);

        Task<ValidationResult> ValidateAsync(SubmitCaptchaRequest command);
    }
}
