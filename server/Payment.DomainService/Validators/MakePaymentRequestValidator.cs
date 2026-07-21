using FluentValidation;
using Payment.DomainService.Requests;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Validators;

public sealed class MakePaymentRequestValidator : AbstractValidator<MakePaymentRequest>
{
    public MakePaymentRequestValidator()
    {
        RuleFor(x => x.ProviderName)
            .NotEmpty()
            .Must(x => string.Equals(x, PaymentConstants.AdyenOnlineProvider, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only ADYEN-ONLINE is supported.");
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(999_999_999m);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.OrderId).NotEmpty().MaximumLength(80);
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

        RuleFor(x => x.RecurringModel)
            .Empty()
            .WithMessage("RecurringModel is managed by the Hosted Checkout flow.");
    }
}
