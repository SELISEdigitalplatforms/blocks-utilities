using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Subscription.DomainService.Utilities;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the subscription capability. Called by both the Api and the Worker, because
    /// the same services back the request path and the background sweeps.
    /// </summary>
    public static IServiceCollection RegisterSubscriptionDomainServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SubscriptionOptions>(
            configuration.GetSection(SubscriptionOptions.SectionName));

        return services;
    }
}
