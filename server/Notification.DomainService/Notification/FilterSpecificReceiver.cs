using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification
{
    public class FilterSpecificReceiver : IStrategicClientProvider
    {
        public ILogger<FilterSpecificReceiver> _logger;
        public INotificationRepository _repository;
        private readonly IHubContext<NotificationHub> _hubContext;

        public FilterSpecificReceiver(INotificationRepository repository, 
                                      ILogger<FilterSpecificReceiver> logger,
                                      IHubContext<NotificationHub> hubContext)
        {
            _logger = logger;
            _repository = repository;
            _hubContext = hubContext;
        }

        public async Task<IClientProxy> GetClientAsync(NotifierPayload notifierPayload)
        {
            var connectionIds = await GetConnectionIdsByFiltersAsync(notifierPayload.SubscriptionFilters);
            var userIds = await GetUserIdsByConnectionIds(connectionIds);

            if (userIds.Count != 0)
                notifierPayload.UserIds = userIds;
            return _hubContext.Clients.Clients(connectionIds);
        }


        private async Task<List<string>> GetUserIdsByConnectionIds(List<string> connectionIds)
        {
            var userIds = new List<string>();

            if (connectionIds != null && connectionIds.Count != 0)
            {
                var guids = (await _repository.GetItemsAsync<NotificationConnection>(p => connectionIds.Contains(p.ConnectionId))).Select(p => p.UserId);
                userIds = guids.ToList();
            }
                
            return userIds.Distinct().ToList();
        }

        private async Task<List<string>> GetConnectionIdsByFiltersAsync(List<SubscriptionFilter> subscriptionFilters)
        {
            var connectionIds = new List<string>();
            foreach (var subscriptionFilter in subscriptionFilters)
            {
                var ids = await GetConnectionIdsByFilter(subscriptionFilter);
                connectionIds.AddRange(ids);
            }
            return connectionIds.ToList();
        }

        public async Task<List<string>> GetConnectionIdsByFilter(SubscriptionFilter subscription)
        {
            var coRelationIds =  (await _repository.GetItemsAsync<NotificationSubscription>(p =>
                    (p.Context == subscription.Context && p.ActionName == subscription.ActionName && p.Value == subscription.Value)
                    || (p.Context == subscription.Context && p.ActionName == subscription.ActionName && p.Value == "")
                    || (p.Context == subscription.Context && p.ActionName == "" && p.Value == "")
                )).Select(p => p.ConnectionId).Distinct();

            return coRelationIds.ToList();
        }
    }
}
