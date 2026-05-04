using DomainService.Configuration;
using DomainService.Entities;
using DomainService.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;


namespace DomainService.Notification
{
    public class FirebaseNotificationServiceProvider : INotifier
    {
        private readonly ILogger<FirebaseNotificationServiceProvider> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        private static readonly HttpClient HttpClient = new HttpClient();
        private const string DefaultMediaType = "application/json";

        public FirebaseNotificationServiceProvider(ILogger<FirebaseNotificationServiceProvider> logger,
                                                   INotificationRepository notificationRepository,
                                                   IConfiguration configuration,
                                                   HttpClient? httpClient = null)
        {
            _logger = logger;
            _configuration = configuration;
            _notificationRepository = notificationRepository;
            _httpClient = httpClient ?? HttpClient;
        }


        public async Task Notify(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(notifyRequest.DenormalizedPayload))
            {
                _logger.LogWarning("Firebase: DenormalizedPayload is required");
                await Task.CompletedTask;
            }

            var userIds = notifyRequest.UserIds;
            var config = await _notificationRepository.GetItemAsync<FirebaseConfiguration>(x => true);

            if (config == null)
            {
                _logger.LogWarning("Firebase: Firebase config not found");
                await Task.CompletedTask;
            }

            if (config != null && string.IsNullOrWhiteSpace(config.AuthorizationKey))
            {
                _logger.LogWarning("Firebase: AuthKey not found");
                await Task.CompletedTask;
            }

            var firebaseUrl = _configuration["FirebaseUri"];

            foreach (var userId in userIds)
            {
                if (config == null) return;
                var applicationId = config.AuthorizationKey;

                var data = new
                {
                    to = $"/topics/{userId}",
                    data = JsonConvert.DeserializeObject(notifyRequest.DenormalizedPayload),
                    notification = JsonConvert.DeserializeObject(notifyRequest.DenormalizedPayload),
                    content_available = notifyRequest.ContentAvailable
                };

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, firebaseUrl)
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(data), Encoding.UTF8, DefaultMediaType)
                };

                requestMessage.Headers.TryAddWithoutValidation("Authorization", $"key={applicationId}");
                requestMessage.Headers.TryAddWithoutValidation("Content-Type", "application/json");

                _logger.LogInformation("Send Data To Firebase:" + System.Text.Json.JsonSerializer.Serialize(data));
                var response = await _httpClient.SendAsync(requestMessage);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(System.Text.Json.JsonSerializer.Serialize(response));
                    continue;
                }

                var responseStr = await response.Content.ReadAsStringAsync();
                var exception = new Exception(responseStr);

                _logger.LogError("Notify: Failed to send notification using firebase {exception}", exception);
                throw exception;
            }

            _logger.LogDebug("Notify-Firebase: Notification has sent successfully by Firebase");
        }
    }
}
