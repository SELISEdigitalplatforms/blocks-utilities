using FluentValidation;
using Iam.DomainService.Configurations;
using Iam.DomainService.Services;

namespace Iam.DomainService.Accounts
{
    public class ChangePasswordValidator : PasswordValidator<ChangePasswordRequest>
    {
        public ChangePasswordValidator(IIamConfigurationRepository configurationRepository, IIdentityAccessManagementRepository identityAccessManagementRepository)
            : base(identityAccessManagementRepository, configurationRepository)
        {
            RuleFor(u => u.OldPassword)
                .NotEmpty()
                .NotNull();

            RuleFor(u => u.NewPassword)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .MustAsync(BeAStrongPassword)
                .WithMessage("Password weak. Ensure at least one lower and upper case letter, one special character, one digit and minimum 8 characters length")
                .MustAsync(CheckBlackListPassword)
                .WithMessage("This password can not be used.");
        }
    }
}
