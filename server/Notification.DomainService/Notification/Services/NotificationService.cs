using Blocks.Genesis;
using DomainService.Configuration.Services;
using DomainService.Entities;
using DomainService.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using StackExchange.Redis;

namespace DomainService.Notification
{
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly IValidator<Subscription> _subscriptionValidator;
        private readonly IValidator<NotifyRequest> _notifyRequestValidator;
        private readonly ILogger<NotificationService> _logger;
        private readonly INotifierServiceFactory _notifierFactory;
        private readonly IConfigurationRepository _configurationRepository;

        public NotificationService(INotificationRepository notificationRepository,
                                   IValidator<Subscription> validator,
                                   IValidator<NotifyRequest> notifyRequestValidator,
                                   ILogger<NotificationService> logger,
                                   INotifierServiceFactory notifierFactory,
                                   IConfigurationRepository configurationRepository)
        {
            _notificationRepository = notificationRepository;
            _subscriptionValidator = validator;
            _notifyRequestValidator = notifyRequestValidator;
            _notifierFactory = notifierFactory;
            _configurationRepository = configurationRepository;
            _logger = logger;
        }

        public async Task<BaseResponse> AddSubscriptionAsync(Subscription request)
        {
            var validationResult = await _subscriptionValidator.ValidateAsync(request);

            if (!validationResult.IsValid)
            {
                return new BaseResponse { Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            var subscriptionFilters = request.Payload.SubscriptionFilters.Select(subscription => new NotificationSubscription
            {
                Id = Guid.NewGuid().ToString(),
                UserId = request.Payload.UserIds.FirstOrDefault(),
                ConnectionId = request.Payload.ConnectionId,
                ActionName = subscription.ActionName,
                Context = subscription.Context,
                Value = subscription.Value,
                CreatedTime = DateTime.UtcNow
            });

            await _notificationRepository.SaveAsync(subscriptionFilters.ToList());

            return new BaseResponse { IsSuccess = true };

        }

        public async Task CreateConnectionAsync(string connectionId)
        {
            var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;

            var connection = new NotificationConnection
            {
                ConnectionId = connectionId,
                UserId = !string.IsNullOrWhiteSpace(userId) ? userId : null,
                CreatedTime = DateTime.UtcNow,
                Id = Guid.NewGuid().ToString(),
            };

            await _notificationRepository.SaveAsync(connection);
        }

        public async Task<BaseResponse> NotifyAsync(NotifyRequest notifyRequest)
        {
            var validateResult = await _notifyRequestValidator.ValidateAsync(notifyRequest);

            if (!validateResult.IsValid)
            {
                return new BaseResponse { IsSuccess = false, Errors = validateResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            var configuration = await _configurationRepository.GetByNameAsync(notifyRequest.ConfiguratoinName);
            await SendNotificationAsync(configuration, notifyRequest);

            //TODO
            //await PublishEventForNotification(notifyRequest);

            return new BaseResponse { IsSuccess = true };
        }

        private async Task SendNotificationAsync(NotificationConfiguration configuration, NotifyRequest notifyRequest)
        {
            var provider = _notifierFactory.GetNotifierServiceProvider(configuration.ChannelToNotify);
            await provider.Notify(notifyRequest, configuration);

            _logger.LogInformation("Notify: Notify request has handled successfully with payload {@payload}", notifyRequest);
        }

        public async Task RemoveCollectionAsync(string collectionId)
        {
            await Task.WhenAll(_notificationRepository.DeleteAsync<NotificationConnection>(p => p.ConnectionId == collectionId),
                               _notificationRepository.DeleteAsync<NotificationSubscription>(p => p.ConnectionId == collectionId));
        }

        public async Task<BaseResponse> RemoveSubscriptionAsync(Subscription subscription)
        {
            var validationResult = await _subscriptionValidator.ValidateAsync(subscription);

            if (!validationResult.IsValid)
            {
                return new BaseResponse { Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            if (subscription?.Payload?.SubscriptionFilters != null)
            {
                foreach (var subscriptionFilter in subscription.Payload.SubscriptionFilters)
                {
                    await _notificationRepository.DeleteAsync<NotificationSubscription>(s =>
                        s.ConnectionId == subscription.Payload.ConnectionId
                        && s.Context == subscriptionFilter.Context
                        && s.ActionName == subscriptionFilter.ActionName
                        && s.Value == subscriptionFilter.Value);
                }
            }

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter(GetUnreadNotificationsRequestBySubscriptionFilter request)
        {
            return request.OrderBy switch
            {
                OfflineNotificationOrder.CreatedTime => await GetUnReadNotificationsByUserIdOrderByCreatedDateDesc(request.UserId, request.SubscriptionFilterData),
                OfflineNotificationOrder.ReadStatus => await GetUnReadNotificationsByUserIdOrderByReadStatus(request.UserId, request.SubscriptionFilterData),
                _ => []
            };
        }

        private async Task<List<OfflineNotification>> GetUnReadNotificationsByUserIdOrderByReadStatus(string userId, SubscriptionFilter subscriptionFilterData)
        {
            var offlineNotifications = (await _notificationRepository.GetItemsAsync<OfflineNotification>(p => (p.Payload.UserId == userId || p.Payload.NotificationType == NotificationReceiverTypes.BroadcastReceiverType.ToString())
                                       && p.Payload.SubscriptionFilters.Contains(subscriptionFilterData))).ToList();

            if (offlineNotifications.Any())
            {
                offlineNotifications = offlineNotifications.Select(p => new OfflineNotification
                {
                    Id = p.Id,
                    Payload = p.Payload,
                    CreatedTime = p.CreatedTime,
                    ReadByUserIds = p.ReadByUserIds,
                    IsRead = p.ReadByUserIds?.Contains(userId.ToString()) ?? false,
                    DenormalizedPayload = p.DenormalizedPayload
                }).OrderBy(p => p.IsRead).ToList();
            }

            return offlineNotifications;
        }

        private async Task<List<OfflineNotification>> GetUnReadNotificationsByUserIdOrderByCreatedDateDesc(string userId, SubscriptionFilter subscriptionFilterData)
        {

            var statusList = !string.IsNullOrWhiteSpace(subscriptionFilterData?.Context)
                ? subscriptionFilterData.Context.Split(',')
                : null;
            List<OfflineNotification> offlineNotifications = [];

            offlineNotifications = statusList != null && statusList.Length > 1
                ? await GetNotificationForMultiContext(statusList, userId, subscriptionFilterData)
                : await GetNotificationForSingleContext(userId, subscriptionFilterData);

            var notifications = offlineNotifications.Select(p => new OfflineNotification
            {
                Id = p.Id,
                Payload = p.Payload,
                CreatedTime = p.CreatedTime,
                ReadByUserIds = p.ReadByUserIds,
                IsRead = p.ReadByUserIds?.Contains(userId.ToString()) ?? false,
                DenormalizedPayload = p.DenormalizedPayload
            });

            return notifications.ToList();
        }

        private async Task<List<OfflineNotification>> GetNotificationForMultiContext(string[] statusList, string userId, SubscriptionFilter subscriptionFilterData)
        {
            if (subscriptionFilterData == null)
                return [];

            var statList = statusList.ToList();
            var notificationList = (await _notificationRepository.GetItemsAsync<OfflineNotification>(p =>
                                              (p.Payload.UserId == userId || p.Payload.NotificationType == NotificationReceiverTypes.BroadcastReceiverType.ToString()) &&
                                               p.Payload.SubscriptionFilters.Any(sf =>
                                               statList.Contains(sf.Context) &&
                                               sf.ActionName == subscriptionFilterData.ActionName && sf.Value == subscriptionFilterData.Value))).OrderByDescending(f => f.CreatedTime);

            return [.. notificationList];
        }

        private async Task<List<OfflineNotification>> GetNotificationForSingleContext(string userId, SubscriptionFilter subscriptionFilterData)
        {
            if (subscriptionFilterData == null)
                return [];

            var notificationList = (await _notificationRepository.GetItemsAsync<OfflineNotification>(p =>
                                                    (p.Payload.UserId == userId || p.Payload.NotificationType == NotificationReceiverTypes.BroadcastReceiverType.ToString()) &&
                                                    p.Payload.SubscriptionFilters.Any(sf =>
                                                    sf.ActionName == subscriptionFilterData.ActionName && sf.Value == subscriptionFilterData.Value))).OrderByDescending(p => p.CreatedTime);

            return [.. notificationList];
        }

        public async Task<BaseResponse> MarkAllNotificationAsRead()
        {
            try
            {
                string userId = BlocksContext.GetContext()?.UserId ?? string.Empty;
                await _notificationRepository.UpdateNotificationAsReadByUserIdAsync(userId);
                return new BaseResponse { IsSuccess = true };
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while marking notificationAsRead {errorMessage: } {stackTrace}", ex.Message, ex.StackTrace);
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string>() { { "Exception", $" {ex.Message}" } } };
            }
        }

        public async Task<BaseResponse> MarkNotificationAsRead(MarkNotificationAsReadRequest request)
        {
            try
            {
                string userId = BlocksContext.GetContext()?.UserId ?? string.Empty;
                await _notificationRepository.UpdateNotificationAsReadByUserIdAsync(userId, request.Id);
                return new BaseResponse { IsSuccess = true };
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while marking notificationAsRead {errorMessage: } {stackTrace}", ex.Message, ex.StackTrace);
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string>() { { "Exception", $" {ex.Message}" } } };
            }
        }

        public async Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request)
        {
            return await _notificationRepository.GetNotificationsAsync(request);
        }
    }
}
