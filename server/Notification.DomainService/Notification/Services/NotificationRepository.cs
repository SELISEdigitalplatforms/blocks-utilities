using Blocks.Genesis;
using DomainService.Shared;
using MongoDB.Driver;
using System.Linq.Expressions;

namespace DomainService.Notification
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        private const string _notificationCollection = "OfflineNotifications";

        public NotificationRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public void Save<T>(T data, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            collection.InsertOne(data);
        }

        public async Task<T> GetItemAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            var collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            var item = await collection.FindAsync(filter);
            return await item.FirstOrDefaultAsync();
        }

        public async Task<List<T>> GetItemsAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            var collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            var items = await collection.FindAsync(filter);
            return await items.ToListAsync();
        }

        public async Task SaveAsync<T>(T data, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            await collection.InsertOneAsync(data);
        }

        public async Task SaveAsync<T>(List<T> listOfData)
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(typeof(T).Name + "s");
           await collection.InsertManyAsync(listOfData);
        }

        public async Task DeleteAsync<T>(Expression<Func<T, bool>> dataFilters)
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(typeof(T).Name + "s");
            await collection.DeleteManyAsync(dataFilters);
        }

        public IQueryable<T> GetItems<T>()
        {
           return _dbContextProvider.GetCollection<T>(typeof(T).Name + "s").AsQueryable();
        }

        public async Task UpdateNotificationAsReadByUserIdAsync(string userId)
        {
            var builder = Builders<OfflineNotification>.Filter;
            var collection = _dbContextProvider.GetCollection<OfflineNotification>(_notificationCollection);

            // Match all notifications visible to this user (aligned with GetNotificationsAsync scope)
            var userNotificationFilter = builder.Or(
                builder.Eq(q => q.Payload.UserId, null),
                builder.Eq(q => q.Payload.UserId, ""),
                builder.Eq(q => q.Payload.UserId, userId)
            );

            // Exclude notifications already read by this user
            var alreadyReadFilter = builder.Where(p => p.ReadByUserIds.Contains(userId));
            var unreadFilter = userNotificationFilter & builder.Not(alreadyReadFilter);

            // First, initialize null ReadByUserIds to empty list (required for $addToSet)
            var nullReadByUserIdsFilter = unreadFilter & builder.Eq(q => q.ReadByUserIds, null);
            await collection.UpdateManyAsync(nullReadByUserIdsFilter,
                new UpdateDefinitionBuilder<OfflineNotification>().Set(p => p.ReadByUserIds, new List<string>()));

            // Then add userId to ReadByUserIds for all unread notifications
            var updateDefinition = new UpdateDefinitionBuilder<OfflineNotification>().AddToSet(p => p.ReadByUserIds, userId);
            await collection.UpdateManyAsync(unreadFilter, updateDefinition);
        }

        public async Task UpdateNotificationAsReadByUserIdAsync(string userId, string notificationId)
        {
            var builder = Builders<OfflineNotification>.Filter;
            var filter = builder.Where(p => p.Id == notificationId);

            var updateDefinition = new UpdateDefinitionBuilder<OfflineNotification>().AddToSet(p => p.ReadByUserIds,
                userId.ToString());

            await _dbContextProvider.GetCollection<OfflineNotification>(_notificationCollection).UpdateOneAsync(filter, updateDefinition);
        }

        public async Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request)
        {
            var collection = _dbContextProvider.GetCollection<OfflineNotification>("OfflineNotifications");
            var builder = Builders<OfflineNotification>.Filter;
            var filter = FilterDefinition<OfflineNotification>.Empty;
            var userId = BlocksContext.GetContext()?.UserId;

            if (request.IsUnreadOnly)
                filter = builder.Where(n => !n.ReadByUserIds.Contains(userId));

            filter = filter & builder.Where(n => string.IsNullOrWhiteSpace(n.Payload.UserId) || n.Payload.UserId == userId);

            var options = new FindOptions<OfflineNotification>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize,
                Sort = Builders<OfflineNotification>.Sort.Descending(n => n.CreatedTime)
            };

            var notifications = await (await collection.FindAsync(filter, options)).ToListAsync();
            var unReadNotificationsCount = await collection.CountDocumentsAsync(filter & builder.Where(n => !n.ReadByUserIds.Contains(userId)));
            var totalNotificationCount =  await collection.CountDocumentsAsync(filter);

            if (!request.IsUnreadOnly)
            {
                foreach (var notification in notifications)
                {
                    notification.IsRead = notification.ReadByUserIds != null && notification.ReadByUserIds.Contains(userId);
                }
            }

            return new GetNotificationsResponse
            {
                Notifications = notifications,
                UnReadNotificationsCount = unReadNotificationsCount,
                TotalNotificationsCount = totalNotificationCount
            };
        }
    }
}
