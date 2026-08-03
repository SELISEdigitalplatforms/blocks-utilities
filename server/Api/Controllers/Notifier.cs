using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class NotifierController : ControllerBase
    {
        private readonly INotificationService _notificationService;

        public NotifierController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> Notify([FromBody] NotifyRequest notifyRequest)
        {
            return await _notificationService.NotifyAsync(notifyRequest);
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [SecretEndPoint]
        [HttpPost]
        public async Task<BaseResponse> SendSecretNotification([FromBody] NotifyRequest notifyRequest)
        {
            return await _notificationService.NotifyAsync(notifyRequest);
        }

        [HttpGet]
        [Authorize]
        public async Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter([FromBody] GetUnreadNotificationsRequestBySubscriptionFilter request)
        {
            return await _notificationService.GetUnreadNotificationsBySubscriptionFilter(request);
        }

        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> MarkAllNotificationAsRead()
        {
            return await _notificationService.MarkAllNotificationAsRead();
        }

        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> MarkNotificationAsRead([FromBody] MarkNotificationAsReadRequest request)
        {
            return await _notificationService.MarkNotificationAsRead(request);
        }

        [HttpGet]
        [Authorize]
        public async Task<GetNotificationsResponse> GetNotifications([FromQuery] GetNotificationsRequest request)
        {
            return await _notificationService.GetNotificationsAsync(request);
        }
    }
}
