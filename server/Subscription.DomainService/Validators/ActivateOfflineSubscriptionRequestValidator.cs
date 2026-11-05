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
        ArgumentNullException.ThrowIfNull(currency);

        RuleFor(request => request.OrganizationId).NotEmpty().MaximumLength(128);

        RuleFor(request => request.ToCreateRequest())
            .SetValidator(new CreateSubscriptionRequestValidator())
            .OverridePropertyName(string.Empty);

        RuleFor(request => request.Pricing).NotNull();

        RuleFor(request => request.Pricing.Mode)
            .IsInEnum()
            .When(request => request.Pricing is not null);

        When(request => request.Pricing is { Mode: OfflinePricingMode.Contract }, () =>
        {
            RuleFor(request => request.Pricing.ContractAmountMinor)
                .NotNull()
                .GreaterThan(0)
                .WithErrorCode("subscription_offline_contract_amount_invalid");

            RuleFor(request => request.Pricing.ContractCurrencyCode)
                // Stop at the first failure: the currency lookup below must never see a null.
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                // A currency whose minor units are unknown cannot be formatted on an invoice, so it
                // is refused here rather than discovered when the first document is rendered.
                .Must(code => currency.TryConvertBack(1, code!.Trim(), out _))
                .WithMessage("The contract currency is not one this tenant's payments know.")
                .WithErrorCode("subscription_offline_contract_currency_unknown");
        });

        When(request => request.Pricing is { Mode: not OfflinePricingMode.Contract }, () =>
        {
            RuleFor(request => request.Pricing.ContractAmountMinor)
                .Null()
                .WithMessage("A contract amount is only accepted with contract pricing.");

            RuleFor(request => request.Pricing.ContractCurrencyCode)
                .Null()
                .WithMessage("A contract currency is only accepted with contract pricing.");
        });
    }
}
