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

        // A rate without a mode is the one combination that cannot be interpreted: the same number
        // means two prices that differ by the tax. Refused at authoring time, where somebody can
        // answer the question, rather than defaulted here and discovered on an invoice.
        //
        // Only for a *positive* rate. Zero and absent both mean untaxed, and demanding a mode for
        // "no tax" would be asking how to add nothing.
        RuleFor(request => request.TaxMode)
            .NotNull()
            .When(request => request.TaxRateBasisPoints > 0)
            .WithMessage(
                "Say whether this tax rate is added to the amount (exclusive) or already included " +
                "in it (inclusive).")
            .WithErrorCode("subscription_price_tax_mode_required");

        RuleFor(request => request.TaxMode!.Value)
            .IsInEnum()
            .When(request => request.TaxMode.HasValue);

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
