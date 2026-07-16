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

    public bool IsValid(PaymentDetail payment, HostedCheckoutResult checkoutResult)
    {
        var amount = checkoutResult.Amount ??
                     checkoutResult.Payments.FirstOrDefault()?.Amount;
        var expectedReference = payment.InitiationRequest?.Reference ??
                                payment.ItemId;

        return _minorUnitResolver.TryConvert(
                   payment.PreciseAmount,
                   payment.CurrencyCode,
                   out var expectedMinorUnits) &&
               string.Equals(checkoutResult.Id, payment.SessionId, StringComparison.Ordinal) &&
               string.Equals(checkoutResult.Reference, expectedReference, StringComparison.Ordinal) &&
               amount?.Value == expectedMinorUnits &&
               string.Equals(
                   amount.Currency,
                   payment.CurrencyCode,
                   StringComparison.OrdinalIgnoreCase);
    }
}
