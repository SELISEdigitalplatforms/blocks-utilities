using FluentValidation;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class UpdateMerchantProfileRequestValidator :
    AbstractValidator<UpdateMerchantProfileRequest>
{
    private static readonly string[] SupportedProviders =
    [
        PaymentConstants.StripeProvider,
        PaymentConstants.AdyenOnlineProvider
    ];

    public UpdateMerchantProfileRequestValidator()
    {
        RuleFor(request => request.LegalName).NotEmpty().MaximumLength(200);

        // Only the two providers this build actually routes subscription charges through --
        // matched case-insensitively the same way the console's own drop-down and the catalog's
        // IsRegistered lookup do, so this can never accept a value readiness would then refuse.
        RuleFor(request => request.PaymentProviderName)
            .NotEmpty()
            .Must(name => SupportedProviders.Any(
                supported => string.Equals(supported, name, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("The payment provider must be STRIPE or ADYEN-ONLINE.");
        RuleFor(request => request.DisplayName).MaximumLength(200);
        RuleFor(request => request.TaxRegistrationId).MaximumLength(64);
        RuleFor(request => request.SupportEmail)
            .EmailAddress()
            .MaximumLength(320)
            .When(request => !string.IsNullOrWhiteSpace(request.SupportEmail));

        // Generous, because this is where bank details and payment terms go and both run long. Still
        // bounded: it is rendered into a PDF, and an unbounded field is an unbounded document.
        RuleFor(request => request.PaymentInstructions).MaximumLength(2000);

        RuleFor(request => request.LogoFileId).MaximumLength(200);

        // Six hex digits, an optional leading '#'. Normalization -- forcing the '#' and the case --
        // happens in the service, not here: a validator's job is to say whether the input is usable,
        // not to rewrite it, and rewriting inside a rule would make what actually gets stored
        // invisible to anything that only reads the validator.
        RuleFor(request => request.PrimaryColor)
            .Matches("^#?[0-9A-Fa-f]{6}$")
            .WithMessage("A color must be a six-digit hex value, e.g. #17365D.")
            .When(request => !string.IsNullOrWhiteSpace(request.PrimaryColor));
        RuleFor(request => request.AccentColor)
            .Matches("^#?[0-9A-Fa-f]{6}$")
            .WithMessage("A color must be a six-digit hex value, e.g. #D9E7F5.")
            .When(request => !string.IsNullOrWhiteSpace(request.AccentColor));

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
