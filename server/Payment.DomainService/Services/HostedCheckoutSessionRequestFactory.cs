using System.Text;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public sealed class HostedCheckoutSessionRequestFactory : IHostedCheckoutSessionRequestFactory
{
    public HostedCheckoutSessionRequest Create(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        PaymentDetail payment,
        PaymentProvider provider,
        string returnUrl,
        string providerReference,
        string shopperReference,
        bool includeStoredPaymentMethods,
        long minorUnits)
    {
        var sendShopperReference =
            includeStoredPaymentMethods ||
            request.ShouldSavePaymentMethod;

        return new HostedCheckoutSessionRequest
        {
            MerchantAccount = provider.MerchantId,
            Store = provider.StoreId,
            Amount = new ProviderAmount
            {
                Value = minorUnits,
                Currency = payment.CurrencyCode
            },
            ReturnUrl = returnUrl,
            Reference = providerReference,
            Mode = "hosted",
            ThemeId = provider.ThemeId,
            CountryCode = provider.CountryCode ?? request.CustomerCountry ?? string.Empty,
            CaptureDelayHours = provider.ManualCapture
                ? null
                : provider.CaptureDelayHours,
            AdditionalData = new ProviderAdditionalData
            {
                ManualCapture = provider.ManualCapture
            },
            Metadata = new ProviderMetadata
            {
                TenantReference = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(payment.TenantId)),
                SiteId = provider.SiteId,
                OrganizationId = context.OrganizationId
            },
            StorePaymentMethodMode = request.ShouldSavePaymentMethod
                ? "askForConsent"
                : "disabled",
            RecurringProcessingModel = sendShopperReference
                ? "CardOnFile"
                : null,
            ShopperReference = sendShopperReference
                ? shopperReference
                : null,
            ShopperEmail = request.CustomerEmail,
            ShopperInteraction = "Ecommerce"
        };
    }
}
