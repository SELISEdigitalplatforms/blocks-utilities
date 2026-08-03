namespace Payment.DomainService.Providers;

public sealed class StoredPaymentMethodProviderGatewayResolver :
    IStoredPaymentMethodProviderGatewayResolver
{
    private readonly IReadOnlyList<IStoredPaymentMethodProviderGateway>
        _gateways;

    public StoredPaymentMethodProviderGatewayResolver(
        IEnumerable<IStoredPaymentMethodProviderGateway> gateways)
    {
        _gateways = gateways.ToArray();
    }

    public IStoredPaymentMethodProviderGateway? Resolve(
        string providerName) =>
        _gateways.FirstOrDefault(
            gateway => gateway.Supports(providerName));
}
