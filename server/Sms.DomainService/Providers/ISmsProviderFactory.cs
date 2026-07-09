using Sms.DomainService.Entities;

namespace Sms.DomainService.Providers;

public interface ISmsProviderFactory
{
    ISmsProvider GetProvider(SmsProviderConfiguration configuration);
}
