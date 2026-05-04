
namespace DomainService.Shared
{
    public class GetUnreadNotificationsRequestBySubscriptionFilter
    {
        public string UserId { get; set; }
        public SubscriptionFilter SubscriptionFilterData { get; set; }
        public OfflineNotificationOrder OrderBy { get; set; }
    }
}
