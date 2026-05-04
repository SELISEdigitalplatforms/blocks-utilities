using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using DomainService.Entities;

namespace DomainService.Notification
{
    public class SignalRNotificationServiceProvider : INotifier
    {
        private readonly IStrategicClientProviderFactory _clientFactoryProvider;
        private readonly INotificationRepository _notificationRepository;
        private readonly ILogger<SignalRNotificationServiceProvider> _logger;   

        public SignalRNotificationServiceProvider(IStrategicClientProviderFactory clientFactoryProvider, 
                                                  INotificationRepository notificationRepository,
                                                  ILogger<SignalRNotificationServiceProvider> logger)
        {
            _clientFactoryProvider = clientFactoryProvider;
            _notificationRepository = notificationRepository;
            _logger = logger;
        }

        public async Task Notify(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            var clientFactoryProvider = _clientFactoryProvider.GetStrategicClientProvider(configuration.NotificationType);
            var clients = await clientFactoryProvider.GetClientAsync(notifyRequest);

            await clients.SendAsync(configuration.NotifyMethod, notifyRequest);

            if (configuration.EnablePersistence)
                await PersistNotificationAsync(notifyRequest, configuration);
        }
        
        private async Task PersistNotificationAsync(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            await InjectUserIdsFromSubscriptionFilter(notifyRequest, configuration);
            var offlineNotifications = BuildOfflineNotification(notifyRequest, configuration);
            //TODO: ToBeDetermined
            //AppendDenormalizedPayloadIfAny(notifyRequest, offlineNotifications);

            var splitSize = 1500;

            if (offlineNotifications.Count > splitSize)
            {
                var count = offlineNotifications.Count;
                var pageIndex = 0;

                while (count > 0)
                {
                    var tempList = offlineNotifications.Skip(pageIndex++ * splitSize).Take(splitSize).ToList();
                    await _notificationRepository.SaveAsync(tempList);
                    count -= splitSize;
                }
            }
            else
              await  _notificationRepository.SaveAsync(offlineNotifications);

            _logger.LogInformation("Notify-SaveNotifierHandler: Notification payload has saved successfully");
        }

        private async Task InjectUserIdsFromSubscriptionFilter(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            var userIds = new List<string>();

            if (configuration.EnablePersistence && notifyRequest?.SubscriptionFilters != null)
            {
                foreach (var subscriptionFilter in notifyRequest.SubscriptionFilters)
                {
                    var filterUserIds = await GetUserIdsBySubscriptionFilter(subscriptionFilter);
                    foreach (var userId in filterUserIds)
                    {
                        if (userId != null)
                            userIds.Add(userId);
                    }
                }
            }
               
            if (userIds.Count > 0)
                notifyRequest.UserIds = userIds;
        }

        private async Task<List<string?>> GetUserIdsBySubscriptionFilter(SubscriptionFilter subscriptionFilter)
        {
            var subscriptions = ( await _notificationRepository.GetItemsAsync<NotificationSubscription>(p => p.CreatedTime >= DateTime.Now.AddHours(-3).ToUniversalTime() &&((p.Context == subscriptionFilter.Context && p.ActionName == subscriptionFilter.ActionName && p.Value == subscriptionFilter.Value) || (p.Context == subscriptionFilter.Context && p.ActionName == subscriptionFilter.ActionName && p.Value == "") || (p.Context == subscriptionFilter.Context && p.ActionName == "" && p.Value == "")))).Select(p => p.UserId).Distinct();
            return subscriptions.ToList();
        }

        private static List<OfflineNotification> BuildOfflineNotification(NotifyRequest saveNotification, NotificationConfiguration configuration, List<string>? userIds = null)
        {

            object? payloadAsObject = null;
            userIds = saveNotification.UserIds ?? [];

            if (saveNotification.SaveDenormalizedPayloadAsAnObject && !string.IsNullOrEmpty(saveNotification.DenormalizedPayload))
            {
                BsonDocument doc;
                BsonDocument.TryParse(saveNotification.DenormalizedPayload, out doc);
                payloadAsObject = BsonSerializer.Deserialize<object>(doc?? new BsonDocument());
            }

            string correlationId = Guid.NewGuid().ToString();

            return userIds.Select(userId =>
                new OfflineNotification()
                {
                    Id = Guid.NewGuid().ToString(),
                    CorrelationId = correlationId,
                    CreatedTime = DateTime.UtcNow,
                    Payload = new PayloadData
                    {
                        NotificationType = configuration.NotificationType.ToString(),
                        ResponseKey = saveNotification.ResponseKey,
                        ResponseValue = saveNotification.ResponseValue,
                        UserId = userId,
                        SubscriptionFilters = saveNotification.SubscriptionFilters
                    },
                    DenormalizedPayload = payloadAsObject ?? saveNotification.DenormalizedPayload
                }).ToList();
        }

    }
}
