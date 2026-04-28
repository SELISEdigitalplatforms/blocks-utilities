using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Migration;
using DomainService.Shared;

namespace DomainService.Migration.Services
{
    public interface IMigrationNotificationService
    {
        Task NotifyMigrationCompletionAsync(string trackerId, MigrationServiceNames serviceName, bool isSuccess, string? errorMessage = null);
    }

    public class MigrationNotificationService : IMigrationNotificationService
    {
        private readonly IMessageClient _messageClient;

        public MigrationNotificationService(IMessageClient messageClient)
        {
            _messageClient = messageClient;
        }

        public async Task NotifyMigrationCompletionAsync(string trackerId, MigrationServiceNames serviceName, bool isSuccess, string? errorMessage = null)
        {
            var completionEvent = new MigrationCompletionEvent
            {
                TrackerId = trackerId,
                ServiceName = serviceName.ToString(),
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
                CompletedAt = DateTime.UtcNow
            };

            await _messageClient.SendToMassConsumerAsync(
                new ConsumerMessage<MigrationCompletionEvent>
                {
                    ConsumerName = IdentifierConstants.MigrationCompletionTopic,
                    Payload = completionEvent
                });
        }
    }
}