namespace Payment.DomainService.Providers;

public sealed class StoredPaymentChargeProviderGatewayResolver :
    IStoredPaymentChargeProviderGatewayResolver
{
    private readonly IReadOnlyCollection<IStoredPaymentChargeProviderGateway>
        _gateways;

    public StoredPaymentChargeProviderGatewayResolver(
        IEnumerable<IStoredPaymentChargeProviderGateway> gateways)
    {
        _gateways = gateways.ToArray();
    }

    public IStoredPaymentChargeProviderGateway? Resolve(
        string providerName) =>
        _gateways.FirstOrDefault(gateway =>
            gateway.Supports(providerName));
}
