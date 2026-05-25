using DomainService.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace DomainService.Notification
{
    public class StrategicClientProviderFactory : IStrategicClientProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public StrategicClientProviderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IStrategicClientProvider GetStrategicClientProvider(NotificationReceiverTypes type)
        {
            return type switch
            {
                NotificationReceiverTypes.BroadcastReceiverType => _serviceProvider.GetRequiredService<BroadcastReceiver>(),
                NotificationReceiverTypes.FilterSpecificReceiverType => _serviceProvider.GetRequiredService<FilterSpecificReceiver>(),
                NotificationReceiverTypes.UserSpecificReceiverType => _serviceProvider.GetRequiredService<UserSpecificReceiver>(),

                _ => throw new ArgumentException("Invalid ReceiverType", type.ToString())
            };
        }
    }

    public interface IStrategicClientProviderFactory
    {
        IStrategicClientProvider GetStrategicClientProvider(NotificationReceiverTypes type);
    }
}
