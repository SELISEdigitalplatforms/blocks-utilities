using Blocks.Genesis;
using DomainService.Subscription.RequestModel;
using DomainService.Subscription.ResponseModel;
using DomainService.Subscription.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class SubscriptionController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly ChangeControllerContext _changeControllerContext;

        public SubscriptionController(ISubscriptionService subscriptionService,
                                 ChangeControllerContext changeControllerContext)
        {
            _subscriptionService = subscriptionService;
            _changeControllerContext = changeControllerContext;
        }

        [HttpGet]
        [Authorize]
        public async Task<GetSubscriptionsResponse> Gets([FromQuery] GetSubscriptionsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _subscriptionService.GetSubscriptionsAsync(request);
        }
    }
}
