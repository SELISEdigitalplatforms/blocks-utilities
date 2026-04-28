using Blocks.Genesis;
using CloudConfiguration.DomainService.Notification.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudConfiguration.DomainService.Notification.ResponseModel
{
    public class GetNotificationConfigurationsResponse : BaseResponse
    {
        public long TotalCount { get; set; }
        public List<NotificationConfiguration> Configurations { get; set; }
    }
}
