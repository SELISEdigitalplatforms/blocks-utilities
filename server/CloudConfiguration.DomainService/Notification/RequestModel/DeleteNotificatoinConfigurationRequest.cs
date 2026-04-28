using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Notification.RequestModel
{
    public class DeleteNotificatoinConfigurationRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public string ProjectKey { get; set; }
    }
}
