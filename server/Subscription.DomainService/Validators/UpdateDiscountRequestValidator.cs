using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class UpdateDiscountRequestValidator : CampaignDiscountRequestValidator<UpdateDiscountRequest>
{
    public UpdateDiscountRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.DurationPeriods).GreaterThan(0)
            .When(request => request.DurationPeriods.HasValue);
    }
}
