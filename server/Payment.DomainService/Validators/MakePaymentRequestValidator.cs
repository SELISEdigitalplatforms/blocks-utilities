using FluentValidation;
using Payment.DomainService.Providers;
using Payment.DomainService.Requests;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Validators;

public sealed class MakePaymentRequestValidator : AbstractValidator<MakePaymentRequest>
{
    public MakePaymentRequestValidator(IPaymentProviderCatalog providerCatalog)
    {
        ArgumentNullException.ThrowIfNull(providerCatalog);

        RuleFor(x => x.ProviderName)
            .NotEmpty()
            .Must(providerCatalog.IsRegistered)
            .WithMessage(
                $"Supported providers: {string.Join(", ", providerCatalog.RegisteredProviderNames)}.")
            .WithErrorCode("payment_provider_not_supported");
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(999_999_999m);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.OrderId).NotEmpty().MaximumLength(80);

        // Capped for the same reason the registration request's is: the organization is
        // hashed into that scope's Key Vault secret name.
        RuleFor(x => x.OrganizationId)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.OrganizationId));

        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Language).MaximumLength(10);
        RuleFor(x => x.CustomerEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));
        RuleFor(x => x.CustomerName).MaximumLength(200);
        RuleFor(x => x.CustomerPhone).MaximumLength(50);

        RuleFor(x => x)
            .Must(x => !x.HasConflictingSavePaymentPreferences)
            .WithName(nameof(MakePaymentRequest.SavePaymentMethod))
            .WithMessage("SavePaymentMethod and RememberCard must have the same value when both are supplied.")
            .WithErrorCode("conflicting_save_payment_preferences");

        RuleFor(x => x.PaymentMeansCustomerId)
            .Empty()
            .WithMessage("PaymentMeansCustomerId is not supported for Hosted Checkout.");

        RuleFor(x => x.PaymentMeansPaymentMethodId)
            .Empty()
            .WithMessage("PaymentMeansPaymentMethodId is not supported for Hosted Checkout.");

        RuleFor(x => x.IsRecurring)
            .Equal(false)
            .WithMessage("Merchant-initiated recurring payments are not supported by this endpoint.");

        // Otherwise managed by the Hosted Checkout flow itself (see AdyenInitiationRequestFactory),
        // which defaults an unset value to CardOnFile whenever a token is saved at all. The one
        // caller allowed to declare an explicit model is subscription checkout, whose renewals are
        // scheduled and merchant-initiated -- Adyen's "Subscription" recurring model, not the
        // shopper-initiated CardOnFile a caller saving a card for on-demand reuse gets by default.
        RuleFor(x => x.RecurringModel)
            .Must(value => string.IsNullOrEmpty(value) ||
                           string.Equals(value, PaymentConstants.SubscriptionRecurringModel, StringComparison.Ordinal))
            .WithMessage(
                $"RecurringModel must be left unset, or set to " +
                $"\"{PaymentConstants.SubscriptionRecurringModel}\" for a token that will be " +
                $"charged on a fixed, merchant-driven schedule.");
    }
}
