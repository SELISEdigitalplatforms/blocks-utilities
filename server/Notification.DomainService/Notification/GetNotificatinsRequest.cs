using Blocks.Genesis;

namespace DomainService.Notification
{
    public class GetNotificationsRequest : BaseGetsRequest<string>
    {
        public bool IsUnreadOnly { get; set; }
    }
}
