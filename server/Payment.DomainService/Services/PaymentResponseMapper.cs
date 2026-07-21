using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class PaymentResponseMapper : IPaymentResponseMapper
{
    public PaymentResponse Map(PaymentDetail payment) => new()
    {
        PaymentDetailId = payment.ItemId,
        ProviderName = payment.ProviderName,
        PaymentStatus = payment.PaymentStatus,
        OrderId = payment.OrderId,
        Amount = payment.PreciseAmount != 0 ? payment.PreciseAmount : (decimal)payment.Amount,
        CurrencyCode = payment.CurrencyCode,
        RedirectUrl = payment.RedirectUrl,
        ExpiresAtUtc = payment.ExpirationDate == default ? null : payment.ExpirationDate,
        CheckoutSessionStatus = payment.CheckoutSessionStatus,
        CheckoutResultCode = payment.CheckoutResultCode,
        PaymentFlow = payment.PaymentFlow,
        RecurringProcessingModel =
            payment.RecurringProcessingModel,
        CaptureStatus = payment.CaptureStatus,
        CaptureMode = payment.CaptureMode,
        AuthorizedAmount = payment.AuthorizedAmount,
        CapturedAmount = payment.CapturedAmount,
        RefundedAmount = payment.RefundedAmount,
        PaymentInstrument = payment.PaymentInstrument == null ? null : new PaymentInstrumentResponse
        {
            Type = payment.PaymentInstrument.Type,
            Brand = payment.PaymentInstrument.Brand,
            LastFour = payment.PaymentInstrument.LastFour,
            ExpiryMonth = payment.PaymentInstrument.ExpiryMonth,
            ExpiryYear = payment.PaymentInstrument.ExpiryYear,
            FundingSource = payment.PaymentInstrument.FundingSource,
            IssuerCountry = payment.PaymentInstrument.IssuerCountry,
            IssuerName = payment.PaymentInstrument.IssuerName
        }
    };
}
