using Blocks.Genesis;
using DomainService.Shared;

namespace DomainService.Configuration
{
    public class SaveConfigurationRequest : IProjectKey
    {
        public string Name { get; set; }
        public NotifierTypes ChannelToNotify { get; set; }
        public NotificationReceiverTypes NotificationType { get; set; }
        public bool EnablePersistence { get; set; }
        public string NotifyMethod { get; set; }
        public string? ProjectKey { get ; set ; }
        public bool IsUpdateRequest { get; set; }
    }
}
