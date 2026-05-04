using DomainService.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace DomainService.Notification
{
    public class NotifierServiceFactory : INotifierServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public NotifierServiceFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public INotifier GetNotifierServiceProvider(NotifierTypes notifierType)
        {
            return notifierType switch
            {
                NotifierTypes.Firebase => _serviceProvider.GetRequiredService<FirebaseNotificationServiceProvider>(),
                NotifierTypes.SignalR => _serviceProvider.GetRequiredService<SignalRNotificationServiceProvider>(),
                _ => throw new ArgumentException("Invalid provider", notifierType.ToString())
            };
        }
    }
}
