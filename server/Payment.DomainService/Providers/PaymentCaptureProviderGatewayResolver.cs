namespace Payment.DomainService.Providers;

public sealed class PaymentCaptureProviderGatewayResolver :
    IPaymentCaptureProviderGatewayResolver
{
    private readonly IReadOnlyCollection<
        IPaymentCaptureProviderGateway> _gateways;

    public PaymentCaptureProviderGatewayResolver(
        IEnumerable<IPaymentCaptureProviderGateway> gateways)
    {
        _gateways = gateways.ToArray();
    }

    public IPaymentCaptureProviderGateway? Resolve(
        string providerName) =>
        _gateways.FirstOrDefault(gateway =>
            gateway.Supports(providerName));
}
