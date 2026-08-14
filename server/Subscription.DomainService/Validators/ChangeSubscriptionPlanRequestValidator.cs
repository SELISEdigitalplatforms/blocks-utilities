using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class ChangeSubscriptionPlanRequestValidator
    : AbstractValidator<ChangeSubscriptionPlanRequest>
{
    public ChangeSubscriptionPlanRequestValidator()
    {
        RuleFor(request => request.PlanCode).NotEmpty().MaximumLength(64);

        RuleFor(request => request.PriceId).NotEmpty().MaximumLength(128);

        RuleForEach(request => request.Quantities)
            .ChildRules(quantity =>
            {
                quantity.RuleFor(item => item.ItemKey).NotEmpty().MaximumLength(64);
                quantity.RuleFor(item => item.Quantity).GreaterThanOrEqualTo(0);
            });

        RuleFor(request => request.Quantities)
            .Must(quantities => quantities
                .Select(quantity => quantity.ItemKey)
                .Distinct(StringComparer.Ordinal)
                .Count() == quantities.Count)
            .WithMessage("The same quantity item is listed more than once.")
            .WithErrorCode("subscription_quantity_duplicated");
    }
}
