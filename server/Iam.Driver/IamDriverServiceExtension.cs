using Iam.DomainService.Utilities;
using IamDriver;
using Microsoft.Extensions.DependencyInjection;

namespace Blocks.Extension.DependencyInjection
{
    public static class IamDriverServiceExtension
    {
        public static void RegisterBlocksIamService(this IServiceCollection services)
        {
            services.AddSingleton<IIamDriverService, IamDriverService>();
            services.RegisterSharedServices();
        }
    }
}
