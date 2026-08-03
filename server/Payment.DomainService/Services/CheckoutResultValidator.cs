using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class CheckoutResultValidator : ICheckoutResultValidator
{
    private readonly ICurrencyMinorUnitResolver _minorUnitResolver;

    public CheckoutResultValidator(ICurrencyMinorUnitResolver minorUnitResolver) =>
        _minorUnitResolver = minorUnitResolver;

    public CheckoutResultValidationOutcome Validate(
        PaymentDetail payment,
        HostedCheckoutResult checkoutResult)
    {
        var expectedReference = payment.InitiationRequest?.Reference ??
                                payment.ItemId;

        if (!_minorUnitResolver.TryConvert(
                payment.PreciseAmount,
                payment.CurrencyCode,
                out var expectedMinorUnits) ||
            !string.Equals(
                checkoutResult.Id,
                payment.SessionId,
                StringComparison.Ordinal) ||
            !string.Equals(
                checkoutResult.Reference,
                expectedReference,
                StringComparison.Ordinal))
        {
            return CheckoutResultValidationOutcome.Mismatch;
        }

        var amount = checkoutResult.Amount ??
                     checkoutResult.Payments?.FirstOrDefault()?.Amount;

        if (amount == null)
        {
            return CheckoutResultValidationOutcome.ProviderDataUnavailable;
        }

        return amount.Value == expectedMinorUnits &&
               string.Equals(
                   amount.Currency,
                   payment.CurrencyCode,
                   StringComparison.OrdinalIgnoreCase)
            ? CheckoutResultValidationOutcome.Valid
            : CheckoutResultValidationOutcome.Mismatch;
    }
}
