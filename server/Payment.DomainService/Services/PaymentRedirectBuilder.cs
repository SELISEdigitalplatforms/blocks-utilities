using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentRedirectBuilder : IPaymentRedirectBuilder
{
    public CheckoutCallbackResult Build(PaymentDetail payment, string redirectStatus)
    {
        var uriBuilder = new UriBuilder(payment.FrontendResultUrlSnapshot!);
        var existingQuery = uriBuilder.Query.TrimStart('?');
        var paymentQuery =
            $"paymentDetailId={Uri.EscapeDataString(payment.ItemId)}&" +
            $"status={Uri.EscapeDataString(redirectStatus)}";

        uriBuilder.Query = string.IsNullOrEmpty(existingQuery)
            ? paymentQuery
            : $"{existingQuery}&{paymentQuery}";

        return CheckoutCallbackResult.Redirect(uriBuilder.Uri.AbsoluteUri);
    }
}
