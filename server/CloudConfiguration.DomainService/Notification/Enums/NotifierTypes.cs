using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudConfiguration.DomainService.Notification.Enums
{
    public enum NotifierTypes
    {
        SignalR,
        Firebase
    }

    public enum NotificationReceiverTypes
    {
        NoReceiverType,
        BroadcastReceiverType,
        UserSpecificReceiverType,
        FilterSpecificReceiverType
    }
}
