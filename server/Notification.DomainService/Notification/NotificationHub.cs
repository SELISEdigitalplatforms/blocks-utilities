using Blocks.Genesis;
using DomainService.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;


namespace DomainService.Notification
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(INotificationService notificationService,
                                ILogger<NotificationHub> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        private void SetPrincipal()
        {
            Thread.CurrentPrincipal = Context.User;
            SetupActivity();
        }

        public async override Task OnConnectedAsync()
        {
            try
            {
                SetPrincipal();
                await _notificationService.CreateConnectionAsync(Context.ConnectionId);
                await  base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        public async override Task OnDisconnectedAsync(Exception exception)
        {
            try
            {
                SetPrincipal();
                await _notificationService.RemoveCollectionAsync(Context.ConnectionId);
                await base.OnDisconnectedAsync(exception);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        public async Task Subscribe(string command)
        {
            try
            {
                SetPrincipal();
                var message = JsonSerializer.Deserialize<NotifierPayload>(command);

                var request = new Subscription()
                {
                    Payload =
                    {
                        ConnectionId = Context.ConnectionId,
                        SubscriptionFilters = message?.SubscriptionFilters
                    }
                };

                request.Payload.UserIds = message.UserIds;
                await _notificationService.AddSubscriptionAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        public async Task Unsubscribe(string command)
        {
            try
            {
                SetPrincipal();
                var message = JsonSerializer.Deserialize<NotifierPayload>(command);

                var request = new Subscription()
                {
                    Payload =
                    {
                        ConnectionId = Context.ConnectionId,
                        SubscriptionFilters = message?.SubscriptionFilters
                    }
                };

                request.Payload.UserIds = message.UserIds;
                await _notificationService.RemoveSubscriptionAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        private void SetupActivity()
        {
            if (Activity.Current == null) return;

            var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
            var userId = Context.User?.FindFirst("user_id")?.Value;

            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenantId ?? string.Empty,
                roles: Array.Empty<string>(),
                userId: userId ?? string.Empty,
                userName: string.Empty,
                isAuthenticated: false,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.MinValue,
                email: string.Empty,
                permissions: Array.Empty<string>(),
                phoneNumber: string.Empty,
                displayName: string.Empty,
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTenantId: tenantId
            ));

        }
    }
}
