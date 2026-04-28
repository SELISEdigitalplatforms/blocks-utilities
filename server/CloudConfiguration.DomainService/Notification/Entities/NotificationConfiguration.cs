using Blocks.Genesis;
using CloudConfiguration.DomainService.Notification.Enums;

namespace CloudConfiguration.DomainService.Notification.Entities
{
    public class NotificationConfiguration : BaseEntity
    {
        public string Name { get; set; }
        public NotifierTypes ChannelToNotify { get; set; }
        public NotificationReceiverTypes NotificationType { get; set; }
        public string NotifyMethod { get; set; }
        public bool EnablePersistence { get; set; }
    }
}
