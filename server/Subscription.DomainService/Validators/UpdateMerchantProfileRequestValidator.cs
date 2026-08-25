using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class UpdateMerchantProfileRequestValidator :
    AbstractValidator<UpdateMerchantProfileRequest>
{
    public UpdateMerchantProfileRequestValidator()
    {
        RuleFor(request => request.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.DisplayName).MaximumLength(200);
        RuleFor(request => request.TaxRegistrationId).MaximumLength(64);
        RuleFor(request => request.SupportEmail)
            .EmailAddress()
            .MaximumLength(320)
            .When(request => !string.IsNullOrWhiteSpace(request.SupportEmail));

        // Generous, because this is where bank details and payment terms go and both run long. Still
        // bounded: it is rendered into a PDF, and an unbounded field is an unbounded document.
        RuleFor(request => request.PaymentInstructions).MaximumLength(2000);

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
