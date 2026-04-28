using Blocks.Genesis;
using CloudConfiguration.DomainService.Notification.Enums;

namespace CloudConfiguration.DomainService.Notification.RequestModel
{
    public class SaveNotificatonConfigurationRequest : IProjectKey
    {
        public string Name { get; set; }
        public NotifierTypes ChannelToNotify { get; set; }
        public NotificationReceiverTypes NotificationType { get; set; }
        public bool EnablePersistence { get; set; }
        public string NotifyMethod { get; set; }
        public string? ProjectKey { get; set; }
        public bool IsUpdateRequest { get; set; }
    }
}
