using DomainService.Entities;
using DomainService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Notification
{
    public interface INotifier
    {
        Task Notify(NotifyRequest notifyRequest, NotificationConfiguration configuration);
    }
}
