using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;

namespace DomainService.Notification
{
    public class BroadcastReceiver : IStrategicClientProvider
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public BroadcastReceiver(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task<IClientProxy> GetClientAsync(NotifierPayload notifierPayload)
        {
            return await Task.FromResult(_hubContext.Clients.All);
        }
    }
}
