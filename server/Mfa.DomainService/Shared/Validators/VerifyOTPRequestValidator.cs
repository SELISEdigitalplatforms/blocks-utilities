using FluentValidation;
using Mfa.DomainService.Shared;

namespace Mfa.DomainService.Validators
{
    public class VerifyOtpRequestValidator : AbstractValidator<VerifyOtpRequest>
    {
        public const string MfaRequired = "Mfa_Required";
        public const string MfaMaxLimit = "Mfa_MaxLimit_50_Exceed";
        public const string VerificationCodeRequired = "Verification_Code_Required";
        public const string VerificationCodeLength = "Verification_Code_Length_4_To_6";
        public const string VerificationCodeNumeric = "Verification_Code_Should_Be_Numeric";

        public VerifyOtpRequestValidator()
        {
            RuleFor(x => x.VerificationCode)
              .Cascade(CascadeMode.Stop)
              .NotEmpty().WithMessage(VerificationCodeRequired)
              .Length(4, 6).WithMessage(VerificationCodeLength)
              .Matches("^[0-9]+$").WithMessage(VerificationCodeNumeric);

            RuleFor(x => x.MfaId)
               .Cascade(CascadeMode.Stop)
               .NotEmpty().WithMessage(MfaRequired)
               .MaximumLength(50).WithMessage(MfaMaxLimit);
        }
    }
}
