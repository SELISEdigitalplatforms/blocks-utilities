using DomainService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Notification
{
    public interface INotificationRepository
    {
        Task SaveAsync<T>(T data, string collectionName = "");
        Task<T> GetItemAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "");
        Task<List<T>> GetItemsAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "");
        Task DeleteAsync<T>(Expression<Func<T, bool>> dataFilters);
        Task SaveAsync<T>(List<T> listOfData);
        Task UpdateNotificationAsReadByUserIdAsync(string userId);
        Task UpdateNotificationAsReadByUserIdAsync(string userId, string notificationId);
        Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request);
    }
}
