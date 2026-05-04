using DomainService.Shared;

namespace DomainService.Notification
{
    public interface INotifierServiceFactory
    {
        INotifier GetNotifierServiceProvider(NotifierTypes notifierType);
    }
}
