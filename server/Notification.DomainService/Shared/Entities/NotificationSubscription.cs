using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Shared
{
    public class NotificationSubscription : SubscriptionFilter
    {
        public string Id { get; set; }
        public string ConnectionId { get; set; }
        public string? UserId { get; set; }
        public DateTime CreatedTime { get; set; }
    }
}
