using Blocks.Genesis;
using DomainService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Notification
{
    public interface INotificationService
    {
        Task CreateConnectionAsync(string connectionId);
        Task RemoveCollectionAsync(string collectionId);
        Task<BaseResponse> AddSubscriptionAsync(Subscription request);
        Task<BaseResponse> RemoveSubscriptionAsync(Subscription subscription);
        Task<BaseResponse> NotifyAsync(NotifyRequest notifyRequest);
        Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter(GetUnreadNotificationsRequestBySubscriptionFilter request);
        Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request);
        Task<BaseResponse> MarkAllNotificationAsRead();
        Task<BaseResponse> MarkNotificationAsRead(MarkNotificationAsReadRequest request);
    }
}
