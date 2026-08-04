using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class NotifierController : ControllerBase
    {
        private readonly INotificationService _notificationService;

        public NotifierController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpPost]
        // [ProtectedEndPoint("blocks-utilities::Notifier::Notify")]
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
        // [ProtectedEndPoint("blocks-utilities::Notifier::GetUnreadNotificationsBySubscriptionFilter")]
        [Authorize]
        public async Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter([FromBody] GetUnreadNotificationsRequestBySubscriptionFilter request)
        {
            return await _notificationService.GetUnreadNotificationsBySubscriptionFilter(request);
        }

        [HttpPost]
        // [ProtectedEndPoint("blocks-utilities::Notifier::MarkAllNotificationAsRead")]
        [Authorize]
        public async Task<BaseResponse> MarkAllNotificationAsRead()
        {
            return await _notificationService.MarkAllNotificationAsRead();
        }

        [HttpPost]
        // [ProtectedEndPoint("blocks-utilities::Notifier::MarkNotificationAsRead")]
        [Authorize]
        public async Task<BaseResponse> MarkNotificationAsRead([FromBody] MarkNotificationAsReadRequest request)
        {
            return await _notificationService.MarkNotificationAsRead(request);
        }

        [HttpGet]
        // [ProtectedEndPoint("blocks-utilities::Notifier::GetNotifications")]
        [Authorize]
        public async Task<GetNotificationsResponse> GetNotifications([FromQuery] GetNotificationsRequest request)
        {
            return await _notificationService.GetNotificationsAsync(request);
        }
    }
}
