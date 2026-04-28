using Blocks.Genesis;
using DomainService.Entities;

namespace DomainService.Subscription.ResponseModel
{
    public class GetSubscriptionsResponse : BaseResponse
    {
        public List<ResourceLimit> Subscriptions { get; set; } = [];
    }
}
