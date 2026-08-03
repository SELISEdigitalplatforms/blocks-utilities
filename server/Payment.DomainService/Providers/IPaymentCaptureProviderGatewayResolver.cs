namespace Payment.DomainService.Providers;

public interface IPaymentCaptureProviderGatewayResolver
{
    IPaymentCaptureProviderGateway? Resolve(string providerName);
}
