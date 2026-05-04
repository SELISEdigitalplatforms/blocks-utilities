using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;

namespace DomainService.Notification
{
    public interface IStrategicClientProvider
    {
        Task<IClientProxy> GetClientAsync(NotifierPayload notifierPayload);
    }
}
