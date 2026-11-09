using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

/// <summary>
/// The shape of a standalone credit quote or purchase. Whether the quantity is within the offer's
/// bounds, and whether the currency is priced, are checked against the offer, which this cannot see.
/// </summary>
public sealed class OrganizationCreditPurchaseRequestValidator : AbstractValidator<OrganizationCreditPurchaseRequest>
{
    public OrganizationCreditPurchaseRequestValidator()
    {
        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Quantity)
            .GreaterThan(0)
            .Must(CreditQuantities.BeWhole)
            .WithMessage("Credits are bought in whole numbers.")
            .When(request => string.IsNullOrWhiteSpace(request.PackCode));
        RuleFor(request => request.PackCode).MaximumLength(32);
        RuleFor(request => request.CurrencyCode)
            .NotEmpty()
            .Length(3)
            .WithMessage("Say which of the offer's currencies to pay in, as a three-letter code.");
        RuleFor(request => request.DiscountCode).MaximumLength(64);
        RuleFor(request => request.IdempotencyKey)
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9_-]+$")
            .When(request => request.IdempotencyKey is not null);
    }
}

public sealed class OrganizationGrantCreditsRequestValidator : AbstractValidator<OrganizationGrantCreditsRequest>
{
    public OrganizationGrantCreditsRequestValidator()
    {
        RuleFor(request => request.MeterKey).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Quantity)
            .GreaterThan(0)
            .Must(CreditQuantities.BeWhole)
            .WithMessage("Credits are granted in whole numbers.");
        RuleFor(request => request.Reason)
            .NotEmpty()
            .MaximumLength(500)
            .WithMessage("Say why these credits are being given. It is kept with the grant.");
    }
}
