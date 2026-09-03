using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class PaymentProviderResponseMapper :
    IPaymentProviderResponseMapper
{
    public PaymentProviderResponse Map(PaymentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new PaymentProviderResponse
        {
            PaymentProviderId = provider.ItemId,
            Version = provider.Version,
            ProviderName = provider.ProviderName,
            MerchantId = provider.MerchantId,
            OrganizationId = provider.OrganizationId,
            ApiBaseUrl = provider.ApiBaseUrl,
            ReturnUrl = provider.ReturnUrl,
            FrontendResultUrl = provider.FrontendResultUrl,
            CountryCode = provider.CountryCode,
            ManualCapture = provider.ManualCapture,
            MaxRefundDays = provider.MaxRefundDays,
            StoreId = provider.StoreId,
            IsEnabled = provider.IsEnabled,
            PaymentMethodConfigurationId = provider.PaymentMethodConfigurationId,
            CheckoutPaymentMethodTypes = provider.CheckoutPaymentMethodTypes
        };
    }
}
