
using FluentValidation;
using CloudConfiguration.DomainService.IAM.RequestModel;

namespace CloudConfiguration.DomainService.IAM.Validators
{
    public class SaveIamConfigurationValidator : AbstractValidator<SaveIamConfigurationRequest>
    {
        public SaveIamConfigurationValidator()
        {
            RuleFor(u => u.AccountVerificationUrl)
                .NotEmpty()
                .NotNull();
            RuleFor(u => u.RecoverAccountUrl)
                .NotEmpty()
                .NotNull();
            RuleFor(u => u.AccountActivationUrl)
                .NotEmpty()
                .NotNull();
        }
    }
}
