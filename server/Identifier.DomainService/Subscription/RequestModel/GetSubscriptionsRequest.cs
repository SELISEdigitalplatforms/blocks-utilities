using Blocks.Genesis;

namespace DomainService.Subscription.RequestModel
{
    public class GetSubscriptionsRequest : IProjectKey
    {
        public string? ProjectKey { get ; set ; }
    }
}
