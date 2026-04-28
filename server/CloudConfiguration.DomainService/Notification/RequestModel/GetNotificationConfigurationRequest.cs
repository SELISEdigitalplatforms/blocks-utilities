using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Notification.RequestModel
{
    public class GetNotificationConfigurationRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public string ProjectKey { get; set; }
    }
}
