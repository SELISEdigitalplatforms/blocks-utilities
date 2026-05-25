using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification
{
    public class UserSpecificReceiver : IStrategicClientProvider
    {
        private readonly ILogger<UserSpecificReceiver> _logger;
        private readonly INotificationRepository _repository;
        private readonly IHubContext<NotificationHub> _hubContext;

        public UserSpecificReceiver(INotificationRepository repository,
                                    ILogger<UserSpecificReceiver> logger,
                                    IHubContext<NotificationHub> hubContext)
        {
            _repository = repository;
            _logger = logger;
            _hubContext = hubContext;
        }
        public async Task<IClientProxy> GetClientAsync(NotifierPayload notifierPayload)
        {
            //TODO
            //if (notifierPayload.Roles != null && notifierPayload.Roles.Any())
            //{
            //    notifierPayload.UserIds = GetUserIdsByRoles(notifierPayload.Roles);
            //}

            var connectionIdStrings = (await _repository.GetItemsAsync<NotificationConnection>(p => notifierPayload.UserIds.Contains(p.UserId))).ToList();

            return _hubContext.Clients.Clients(connectionIdStrings.Select(p => p.ConnectionId).ToList());
        }
    }
}
