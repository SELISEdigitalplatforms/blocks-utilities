using Mfa.DomainService.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Blocks.Extensions.DependencyInjection;

public static class MfaDriverServiceExtension
{
    public static void RegisterBlocksMfaService(this IServiceCollection services)
    {
        services.RegisterAllServices();
    }
}
