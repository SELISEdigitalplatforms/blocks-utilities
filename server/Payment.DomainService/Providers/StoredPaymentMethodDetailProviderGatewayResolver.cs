namespace Payment.DomainService.Providers;

public sealed class StoredPaymentMethodDetailProviderGatewayResolver :
    IStoredPaymentMethodDetailProviderGatewayResolver
{
    private readonly IReadOnlyList<IStoredPaymentMethodDetailProviderGateway> _gateways;

    public StoredPaymentMethodDetailProviderGatewayResolver(
        IEnumerable<IStoredPaymentMethodDetailProviderGateway> gateways)
    {
        _gateways = gateways.ToArray();
    }

    public IStoredPaymentMethodDetailProviderGateway? Resolve(string providerName) =>
        _gateways.FirstOrDefault(gateway => gateway.Supports(providerName));
}
