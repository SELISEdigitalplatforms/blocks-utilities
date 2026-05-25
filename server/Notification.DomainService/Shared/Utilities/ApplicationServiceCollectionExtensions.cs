using DomainService.Configuration.Services;
using DomainService.Notification;
using DomainService.Shared;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DomainService.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void RegisterAllNotificationApplicationServices(this IServiceCollection serviceCollection)
        {
            #region Services
            serviceCollection.AddSignalR(); 

            serviceCollection.AddSingleton<INotificationService, NotificationService>();
            serviceCollection.AddSingleton<INotificationRepository, NotificationRepository>();
            serviceCollection.AddSingleton<INotifierServiceFactory, NotifierServiceFactory>();
            serviceCollection.AddSingleton<IStrategicClientProviderFactory, StrategicClientProviderFactory>();
            serviceCollection.AddSingleton<IConfigurationService, ConfigurationService>();
            serviceCollection.AddSingleton<IConfigurationRepository, ConfigurationRepository>();

            serviceCollection.AddSingleton<FirebaseNotificationServiceProvider>();
            serviceCollection.AddSingleton<SignalRNotificationServiceProvider>();
            serviceCollection.AddSingleton<BroadcastReceiver>();
            serviceCollection.AddSingleton<UserSpecificReceiver>();
            serviceCollection.AddSingleton<FilterSpecificReceiver>();

            #endregion

            #region Validators
            serviceCollection.AddTransient<IValidator<Subscription>, AddSubscriptionRequestValidator>();
            serviceCollection.AddTransient<IValidator<NotifyRequest>, NotifyRequestValidator>();
            serviceCollection.AddTransient<IValidator<Configuration.SaveConfigurationRequest>, ConfigurationValidator>();
            #endregion
        }
    }
}
