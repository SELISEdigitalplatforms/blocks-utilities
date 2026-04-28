using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Notification.RequestModel
{
    public class GetNotificationConfigurationsRequest : BaseGetsRequest<string>, IProjectKey
    {
        public string? ProjectKey { get; set; }
    }
}
