namespace Payment.DomainService.Providers;

public sealed class PaymentRefundProviderGatewayResolver :
    IPaymentRefundProviderGatewayResolver
{
    private readonly IReadOnlyCollection<
        IPaymentRefundProviderGateway> _gateways;

    public PaymentRefundProviderGatewayResolver(
        IEnumerable<IPaymentRefundProviderGateway> gateways)
    {
        _gateways = gateways.ToArray();
    }

    public IPaymentRefundProviderGateway? Resolve(
        string providerName) =>
        _gateways.FirstOrDefault(gateway =>
            gateway.Supports(providerName));
}
