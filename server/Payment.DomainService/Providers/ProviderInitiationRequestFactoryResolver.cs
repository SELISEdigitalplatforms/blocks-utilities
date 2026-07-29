namespace Payment.DomainService.Providers;

public sealed class ProviderInitiationRequestFactoryResolver :
    IProviderInitiationRequestFactoryResolver
{
    private readonly IReadOnlyCollection<
        IProviderInitiationRequestFactory> _factories;

    public ProviderInitiationRequestFactoryResolver(
        IEnumerable<IProviderInitiationRequestFactory> factories)
    {
        _factories = factories.ToArray();
    }

    public IProviderInitiationRequestFactory? Resolve(
        string providerName) =>
        _factories.FirstOrDefault(factory =>
            factory.Supports(providerName));
}
