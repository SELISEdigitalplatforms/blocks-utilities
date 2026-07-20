namespace Payment.DomainService.Providers;

public interface IPaymentRefundProviderGatewayResolver
{
    IPaymentRefundProviderGateway? Resolve(
        string providerName);
}
