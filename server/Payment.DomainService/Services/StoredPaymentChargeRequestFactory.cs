using System.Text;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.StoredPayment;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class StoredPaymentChargeRequestFactory :
    IStoredPaymentChargeRequestFactory
{
    public StoredPaymentChargeRequest Create(
        PaymentDetail payment,
        PaymentProvider provider,
        StoredPaymentMethod method,
        string providerReference,
        string providerToken,
        long minorUnits)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(method);

        // Only Adyen reads the capture fields. Stripe's off-session charge confirms without a
        // capture_method, which Stripe captures automatically whatever the provider is set to.
        var isAdyen = string.Equals(
            provider.ProviderName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

        return new StoredPaymentChargeRequest
        {
            MerchantAccount = provider.MerchantId,
            Store = string.IsNullOrWhiteSpace(provider.StoreId) ? null : provider.StoreId,
            CaptureMode = isAdyen
                ? AdyenInitiationRequestFactory.ResolveCaptureMode(provider)
                : PaymentCaptureModes.AutomaticImmediate,
            CaptureDelayHours = isAdyen && !provider.ManualCapture
                ? provider.CaptureDelayHours
                : null,
            AdditionalData = isAdyen && provider.ManualCapture
                ? new ProviderAdditionalData { ManualCapture = true }
                : null,
            Amount = new ProviderAmount
            {
                Value = minorUnits,
                Currency = payment.CurrencyCode
            },
            Reference = providerReference,
            PaymentMethod = new StoredPaymentChargeMethod
            {
                StoredPaymentMethodId = providerToken
            },
            ShopperReference =
                payment.ShopperReference ?? string.Empty,
            ProviderPayerReference = method.ProviderPayerReference,
            ShopperInteraction = "ContAuth",
            RecurringProcessingModel =
                payment.RecurringProcessingModel ??
                "Subscription",
            ShopperStatement =
                string.IsNullOrWhiteSpace(payment.Description)
                    ? null
                    : payment.Description,
            Metadata = new ProviderMetadata
            {
                TenantReference = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(payment.TenantId)),
                SiteId = provider.SiteId,
                OrganizationId = payment.OrganizationId
            }
        };
    }
}
