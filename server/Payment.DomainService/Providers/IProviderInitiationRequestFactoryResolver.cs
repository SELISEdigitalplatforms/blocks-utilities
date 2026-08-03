namespace Payment.DomainService.Providers;

public interface IProviderInitiationRequestFactoryResolver
{
    /// <summary>
    /// The initiation request factory for <paramref name="providerName"/>, or
    /// <see langword="null"/> when no factory serves that provider.
    /// </summary>
    IProviderInitiationRequestFactory? Resolve(string providerName);
}
