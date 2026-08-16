using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class CreatePlanRequestValidator : AbstractValidator<CreatePlanRequest>
{
    private const int MaximumCodeLength = 64;

    public CreatePlanRequestValidator()
    {
        Include(new PlanDefinitionRequestValidator());

        RuleFor(request => request.Code)
            .NotEmpty()
            .MaximumLength(MaximumCodeLength)
            .Matches("^[a-z0-9_-]+$")
            .WithMessage(
                "A plan code may contain only lowercase letters, digits, hyphens and underscores.")
            .WithErrorCode("subscription_plan_code_invalid");
    }
}
