

namespace DomainService.Shared
{
    public class PayloadData
    {
        public string? UserId { get; set; }
        public List<SubscriptionFilter> SubscriptionFilters { get; set; }
        public string NotificationType { get; set; }
        public string ResponseKey { get; set; }
        public string ResponseValue { get; set; }
    }
}
