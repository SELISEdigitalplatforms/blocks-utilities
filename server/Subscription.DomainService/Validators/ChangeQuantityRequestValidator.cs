using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class ChangeQuantityRequestValidator : AbstractValidator<ChangeQuantityRequest>
{
    public ChangeQuantityRequestValidator()
    {
        RuleFor(request => request.Version)
            .GreaterThan(0)
            .WithMessage(
                "Send the version you last read. Without it a stale tab can overwrite a seat " +
                "count somebody else has already changed.");

        RuleFor(request => request.Quantities)
            .NotEmpty()
            .WithMessage("Name at least one quantity item to change.");

        RuleForEach(request => request.Quantities)
            .ChildRules(item =>
            {
                item.RuleFor(quantity => quantity.ItemKey).NotEmpty().MaximumLength(64);
                item.RuleFor(quantity => quantity.Quantity).GreaterThanOrEqualTo(0);
            });

        RuleFor(request => request.Quantities)
            .Must(quantities => quantities
                .Select(quantity => quantity.ItemKey)
                .Distinct(StringComparer.Ordinal)
                .Count() == quantities.Count)
            .WithMessage("The same item appears twice; the intended quantity would be ambiguous.")
            .WithErrorCode("subscription_quantity_duplicated");
    }
}
