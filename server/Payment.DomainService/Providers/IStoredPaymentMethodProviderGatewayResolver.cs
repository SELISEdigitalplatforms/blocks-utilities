namespace Payment.DomainService.Providers;

public interface IStoredPaymentMethodProviderGatewayResolver
{
    IStoredPaymentMethodProviderGateway? Resolve(
        string providerName);
}
