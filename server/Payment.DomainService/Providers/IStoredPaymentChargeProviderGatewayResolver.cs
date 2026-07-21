namespace Payment.DomainService.Providers;

public interface IStoredPaymentChargeProviderGatewayResolver
{
    IStoredPaymentChargeProviderGateway? Resolve(string providerName);
}
