using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscription.DomainService.Repositories;

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

        // Repositories are singletons and take the tenant as an argument, so the same instance
        // serves a request and a background sweep. They hold no per-tenant state beyond the
        // record of which tenants they have already indexed.
        services.AddSingleton<
            ISubscriptionCatalogueRepository,
            SubscriptionCatalogueRepository>();
        services.AddSingleton<
            IBillingAccountRepository,
            BillingAccountRepository>();
        services.AddSingleton<
            ISubscriptionRepository,
            SubscriptionRepository>();
        services.AddSingleton<
            ISubscriptionUsageRepository,
            SubscriptionUsageRepository>();
        services.AddSingleton<
            ISubscriptionPaymentLinkRepository,
            SubscriptionPaymentLinkRepository>();

        return services;
    }
}
