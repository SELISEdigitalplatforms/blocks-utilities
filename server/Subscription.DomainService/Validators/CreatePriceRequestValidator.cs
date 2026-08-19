using FluentValidation;
using Payment.DomainService.Services;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class CreatePriceRequestValidator : AbstractValidator<CreatePriceRequest>
{
    public CreatePriceRequestValidator(ICurrencyMinorUnitResolver currencyResolver)
    {
        ArgumentNullException.ThrowIfNull(currencyResolver);

        RuleFor(request => request.PlanId).NotEmpty();

        RuleFor(request => request.CurrencyCode)
            .NotEmpty()
            .Length(3)
            .Matches("^[A-Za-z]{3}$");

        RuleFor(request => request.UnitAmountMinor).GreaterThanOrEqualTo(0);

        RuleFor(request => request.IntervalCount).InclusiveBetween(1, 36);
        RuleFor(request => request.DisplayPriceNote).MaximumLength(200);

        RuleFor(request => request.TaxRateBasisPoints!.Value)
            .InclusiveBetween(0, 10_000)
            .When(request => request.TaxRateBasisPoints.HasValue);

        RuleFor(request => request)
            .Must(request => IsChargeable(currencyResolver, request))
            .WithName(nameof(CreatePriceRequest.CurrencyCode))
            .WithMessage(
                "This currency is not configured for payments, so a subscription priced in it " +
                "could never be charged.")
            .WithErrorCode("subscription_currency_unsupported");
    }

    /// <summary>
    /// Checks the currency here, where a person is authoring a price and can fix it, rather
    /// than at checkout — where the same misconfiguration surfaces as an opaque payment error
    /// to a customer who has already chosen a plan.
    /// </summary>
    private static bool IsChargeable(
        ICurrencyMinorUnitResolver currencyResolver,
        CreatePriceRequest request) =>
        !string.IsNullOrWhiteSpace(request.CurrencyCode) &&
        currencyResolver.TryConvertBack(
            Math.Max(request.UnitAmountMinor, 1),
            request.CurrencyCode.ToUpperInvariant(),
            out _);
}
