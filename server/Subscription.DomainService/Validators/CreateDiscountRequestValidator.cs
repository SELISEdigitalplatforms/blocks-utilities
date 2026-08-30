using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class CreateDiscountRequestValidator : CampaignDiscountRequestValidator<CreateDiscountRequest>
{
    public CreateDiscountRequestValidator()
    {
        RuleFor(request => request.Code).NotEmpty().MaximumLength(64).Matches("^[a-z0-9_-]+$");
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.DurationPeriods).GreaterThan(0)
            .When(request => request.DurationPeriods.HasValue);
    }
}
