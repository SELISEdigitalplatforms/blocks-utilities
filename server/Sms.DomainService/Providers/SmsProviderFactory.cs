using Sms.DomainService.Entities;

namespace Sms.DomainService.Providers;

public class SmsProviderFactory : ISmsProviderFactory
{
    private readonly IEnumerable<ISmsProvider> _providers;

    public SmsProviderFactory(IEnumerable<ISmsProvider> providers)
    {
        _providers = providers;
    }

    public ISmsProvider GetProvider(SmsProviderConfiguration configuration)
    {
        var provider = _providers.FirstOrDefault(x => x.ProviderType == configuration.ProviderType);
        if (provider == null)
        {
            throw new InvalidOperationException($"SMS provider '{configuration.ProviderType}' is not registered.");
        }

        return provider;
    }
}
