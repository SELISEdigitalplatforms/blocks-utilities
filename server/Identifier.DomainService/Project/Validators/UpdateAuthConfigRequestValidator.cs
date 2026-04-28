using FluentValidation;

namespace DomainService.Projects
{
    public class UpdateAuthConfigRequestValidator : AbstractValidator<UpdateAuthConfigRequest>
    {
        public UpdateAuthConfigRequestValidator()
        {
            RuleFor(x => x.RefreshTokenValidForNumberMinutes)
                .Cascade(CascadeMode.Stop)
                .GreaterThan(0)
                .WithMessage("Value must be greater than zero.");

            RuleFor(x => x.GetNumberOfWrongAttemptsToLockTheAccount)
                .Cascade(CascadeMode.Stop)
                .GreaterThan(0)
                .WithMessage("Value must be greater than zero.");

            RuleFor(x => x.AccountLockDurationInMinutes)
                .Cascade(CascadeMode.Stop)
                .GreaterThan(0)
                .WithMessage("Value must be greater than zero.");

            RuleFor(x => x.ProjectId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("ProjectId is required.")
                .MinimumLength(1).WithMessage("ProjectId cannot be empty.");

            RuleFor(x => x.AllowedGrantTypes)
                 .Cascade(CascadeMode.Stop)
                 .Must(x => x != null && x.Count > 0)
                 .WithMessage("Atleast one grantType is required");
        }
    }
}
