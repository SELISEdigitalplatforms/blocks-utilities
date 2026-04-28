using Cloud.DomainService.Repositories;
using Cloud.DomainService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cloud.DomainService.Utilities
{
    public static class CloudDomainServiceCollectionExtensions
    {
        public static void AddCloudDomainServices(this IServiceCollection services)
        {
            services.AddSingleton<IApiEndpointConfigRepository, ApiEndpointConfigRepository>();
            services.AddSingleton<IApiEndpointConfigService, ApiEndpointConfigService>();
        }
    }
}
