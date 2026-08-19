using FluentValidation;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class CreateDiscountRequestValidator : AbstractValidator<CreateDiscountRequest>
{
    public CreateDiscountRequestValidator()
    {
        RuleFor(request => request.Code).NotEmpty().MaximumLength(64).Matches("^[a-z0-9_-]+$");
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.PercentBasisPoints).NotNull().InclusiveBetween(1, 10_000)
            .When(request => request.Kind == DiscountKind.Percent);
        RuleFor(request => request.AmountMinor).NotNull().GreaterThan(0)
            .When(request => request.Kind == DiscountKind.FixedAmount);
        RuleFor(request => request.CurrencyCode).NotEmpty().Length(3)
            .When(request => request.Kind == DiscountKind.FixedAmount);
        RuleFor(request => request.DurationPeriods).GreaterThan(0)
            .When(request => request.DurationPeriods.HasValue);
        RuleForEach(request => request.ApplicablePlanCodes).NotEmpty().MaximumLength(64);
        RuleFor(request => request).Must(request =>
            request.Kind != DiscountKind.Percent || request.AmountMinor is null)
            .WithMessage("A percentage discount cannot also define a fixed amount.");
        RuleFor(request => request).Must(request =>
            request.Kind != DiscountKind.FixedAmount || request.PercentBasisPoints is null)
            .WithMessage("A fixed discount cannot also define a percentage.");
    }
}
