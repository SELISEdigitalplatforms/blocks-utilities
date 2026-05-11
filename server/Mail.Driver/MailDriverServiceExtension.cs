using Blocks.MailDriver;
using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Blocks.Extension.DependencyInjection;

public static class MailDriverServiceExtension
{
    public static void RegisterBlocksMailService(this IServiceCollection services)
    {
        services.RegisterAllMailApplicationServices();
        services.AddSingleton<IMailDriverService, MailDriverService>();
    }
}
