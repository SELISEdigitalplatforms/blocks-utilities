using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class UpdateBillingProfileRequestValidator :
    AbstractValidator<UpdateBillingProfileRequest>
{
    public UpdateBillingProfileRequestValidator()
    {
        RuleFor(request => request.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.DisplayName).MaximumLength(200);
        RuleFor(request => request.BillingContactName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.BillingContactEmail).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(request => request.TaxRegistrationId).MaximumLength(64);

        // Bounded, not validated for shape. Every jurisdiction spells an address differently and a
        // pattern invented here would refuse a legitimate one; the country code is the single field
        // with an actual standard behind it.
        When(request => request.Address is not null, () =>
        {
            RuleFor(request => request.Address!.Line1).MaximumLength(200);
            RuleFor(request => request.Address!.Line2).MaximumLength(200);
            RuleFor(request => request.Address!.City).MaximumLength(120);
            RuleFor(request => request.Address!.Region).MaximumLength(120);
            RuleFor(request => request.Address!.PostalCode).MaximumLength(32);
            RuleFor(request => request.Address!.CountryCode)
                .Length(2)
                .Matches("^[A-Za-z]{2}$")
                .When(request => !string.IsNullOrWhiteSpace(request.Address!.CountryCode));
        });
    }
}
