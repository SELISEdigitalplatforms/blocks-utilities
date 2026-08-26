using FluentValidation;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Validators;

public sealed class CreateSubscriptionRequestValidator
    : AbstractValidator<CreateSubscriptionRequest>
{
    public CreateSubscriptionRequestValidator()
    {
        RuleFor(request => request.PlanCode).NotEmpty().MaximumLength(64);

        RuleFor(request => request.PriceId).NotEmpty().MaximumLength(128);

        RuleFor(request => request.TimeZoneId)
            .NotEmpty()
            .Must(timeZoneId => BillingLocalTime.TryFindTimeZone(timeZoneId, out _))
            .WithMessage(
                "This time zone is not known to the server. Billing boundaries are computed in " +
                "the customer's own calendar, so an unknown zone has to be refused here rather " +
                "than surfacing at the first renewal.")
            .WithErrorCode("subscription_time_zone_unknown");

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

        RuleFor(request => request.BillingEmail)
            .EmailAddress()
            .When(request => !string.IsNullOrWhiteSpace(request.BillingEmail));
    }
}
