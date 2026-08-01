namespace Payment.DomainService.Providers;

public interface IStoredPaymentMethodDetailProviderGatewayResolver
{
    IStoredPaymentMethodDetailProviderGateway? Resolve(string providerName);
}
