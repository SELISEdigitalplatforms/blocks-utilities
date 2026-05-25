using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Shared
{
    public enum NotificationReceiverTypes
    {
        NoReceiverType,
        BroadcastReceiverType,
        UserSpecificReceiverType,
        FilterSpecificReceiverType
    }
}
