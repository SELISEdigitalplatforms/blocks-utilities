using FluentValidation;
using Payment.DomainService.Services;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

/// <summary>
/// Validates an offline activation: the card-subscriber rules for the terms, plus the pricing.
/// </summary>
/// <remarks>
/// The terms are checked by <see cref="CreateSubscriptionRequestValidator"/> itself rather than a
/// copy of its rules, so an offline subscription can never be created on terms a card subscriber
/// would have been refused.
/// </remarks>
public sealed class ActivateOfflineSubscriptionRequestValidator
    : AbstractValidator<ActivateOfflineSubscriptionRequest>
{
    public ActivateOfflineSubscriptionRequestValidator(ICurrencyMinorUnitResolver currency)
    {
        RuleFor(request => request.OrganizationId).NotEmpty().MaximumLength(128);

        RuleFor(request => request.ToCreateRequest())
            .SetValidator(new CreateSubscriptionRequestValidator())
            .OverridePropertyName(string.Empty);

        RuleFor(request => request.Pricing)
            .NotNull()
            .SetValidator(new OfflinePricingRequestValidator(currency));
    }
}

/// <summary>
/// The pricing an operator names, wherever it is named, so a contract means one thing throughout.
/// </summary>
public sealed class OfflinePricingRequestValidator : AbstractValidator<OfflinePricingRequest>
{
    public OfflinePricingRequestValidator(ICurrencyMinorUnitResolver currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        RuleFor(pricing => pricing.Mode).IsInEnum();

        When(pricing => pricing.Mode == OfflinePricingMode.Contract, () =>
        {
            RuleFor(pricing => pricing.ContractAmountMinor)
                .NotNull()
                .GreaterThan(0)
                .WithErrorCode("subscription_offline_contract_amount_invalid");

            RuleFor(pricing => pricing.ContractCurrencyCode)
                // Stop at the first failure: the currency lookup below must never see a null.
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                // A currency whose minor units are unknown cannot be formatted on an invoice, so it
                // is refused here rather than discovered when the first document is rendered.
                .Must(code => currency.TryConvertBack(1, code!.Trim(), out _))
                .WithMessage("The contract currency is not one this tenant's payments know.")
                .WithErrorCode("subscription_offline_contract_currency_unknown");
        });

        When(pricing => pricing.Mode != OfflinePricingMode.Contract, () =>
        {
            RuleFor(pricing => pricing.ContractAmountMinor)
                .Null()
                .WithMessage("A contract amount is only accepted with contract pricing.");

            RuleFor(pricing => pricing.ContractCurrencyCode)
                .Null()
                .WithMessage("A contract currency is only accepted with contract pricing.");
        });
    }
}
