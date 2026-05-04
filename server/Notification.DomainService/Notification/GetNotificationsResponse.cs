using DomainService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Notification
{
    public class GetNotificationsResponse
    {
        public List<OfflineNotification> Notifications { get; set; } 
        public long UnReadNotificationsCount { get; set; }
        public long TotalNotificationsCount { get; set; }
    }
}
