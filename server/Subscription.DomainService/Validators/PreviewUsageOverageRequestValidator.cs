using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class PreviewUsageOverageRequestValidator : AbstractValidator<PreviewUsageOverageRequest>
{
    public PreviewUsageOverageRequestValidator()
    {
        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);

        RuleFor(request => request.AdditionalQuantity)
            .GreaterThan(0)
            .WithMessage("The additional quantity must be a positive whole number.");
    }
}
