using System.Text;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.StoredPayment;

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

        return new StoredPaymentChargeRequest
        {
            MerchantAccount = provider.MerchantId,
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
