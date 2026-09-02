using FluentValidation;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Validators;

public sealed class PreviewUsageOverageRequestValidator : AbstractValidator<PreviewUsageOverageRequest>
{
    public PreviewUsageOverageRequestValidator()
    {
        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);

        RuleFor(request => request.AdditionalQuantity)
            .GreaterThan(0)
            .WithMessage("The additional quantity must be positive.");

        // Granularity is checked by the service, which is the only place that knows the meter's
        // declared scale. Magnitude can be refused here, before anything is read.
        RuleFor(request => request.AdditionalQuantity)
            .Must(MeterQuantity.IsWithinMagnitude)
            .WithMessage("The additional quantity is larger than a quantity may be.")
            .WithErrorCode("subscription_usage_quantity_scale_invalid");
    }
}
