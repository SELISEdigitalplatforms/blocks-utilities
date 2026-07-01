using System.Text.Json;
using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Mail
{
    public class MailOutboxServiceTests
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        [Fact]
        public async Task PublishPendingAsync_WhenPublishSucceeds_MarksOutboxMessagePublished()
        {
            var repository = new Mock<IMailRepository>();
            var messageClient = new Mock<IMessageClient>();
            var outboxMessage = CreateOutboxMessage();

            repository
                .Setup(x => x.GetPendingOutboxMessagesAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync([outboxMessage]);
            repository
                .Setup(x => x.TryClaimOutboxMessageAsync(outboxMessage.ItemId, It.IsAny<DateTime>()))
                .ReturnsAsync(true);

            var service = CreateService(repository, messageClient);

            var publishedCount = await service.PublishPendingAsync();

            Assert.Equal(1, publishedCount);
            messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<NoAttachmentSendEmailCommand>>(m =>
                m.ConsumerName == CommunicationConstants.NoAttachmentMailQueueName &&
                m.Payload.ItemId == "mail-1" &&
                m.Payload.Attempt == 1)), Times.Once);
            repository.Verify(x => x.MarkOutboxMessagePublishedAsync(outboxMessage.ItemId, It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task PublishPendingAsync_WhenPublishFails_SchedulesRetry()
        {
            var repository = new Mock<IMailRepository>();
            var messageClient = new Mock<IMessageClient>();
            var outboxMessage = CreateOutboxMessage();

            repository
                .Setup(x => x.GetPendingOutboxMessagesAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync([outboxMessage]);
            repository
                .Setup(x => x.TryClaimOutboxMessageAsync(outboxMessage.ItemId, It.IsAny<DateTime>()))
                .ReturnsAsync(true);
            messageClient
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<NoAttachmentSendEmailCommand>>()))
                .ThrowsAsync(new InvalidOperationException("broker unavailable"));

            var service = CreateService(repository, messageClient);

            var publishedCount = await service.PublishPendingAsync();

            Assert.Equal(0, publishedCount);
            repository.Verify(x => x.MarkOutboxMessageFailedAsync(
                outboxMessage.ItemId,
                1,
                It.Is<DateTime>(nextAttemptUtc => nextAttemptUtc > DateTime.UtcNow),
                OutboxMessageStatus.FailedRetryable,
                "broker unavailable"), Times.Once);
        }

        [Fact]
        public async Task PublishPendingAsync_WhenMaxAttemptsReached_DeadLettersMessage()
        {
            var repository = new Mock<IMailRepository>();
            var messageClient = new Mock<IMessageClient>();
            var outboxMessage = CreateOutboxMessage();

            repository
                .Setup(x => x.GetPendingOutboxMessagesAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync([outboxMessage]);
            repository
                .Setup(x => x.TryClaimOutboxMessageAsync(outboxMessage.ItemId, It.IsAny<DateTime>()))
                .ReturnsAsync(true);
            messageClient
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<NoAttachmentSendEmailCommand>>()))
                .ThrowsAsync(new InvalidOperationException("broker unavailable"));

            var service = CreateService(repository, messageClient, new Dictionary<string, string?>
            {
                ["MailOutbox:MaxPublishAttempts"] = "1"
            });

            await service.PublishPendingAsync();

            repository.Verify(x => x.MarkOutboxMessageFailedAsync(
                outboxMessage.ItemId,
                1,
                It.IsAny<DateTime>(),
                OutboxMessageStatus.DeadLettered,
                "broker unavailable"), Times.Once);
        }

        private static MailOutboxService CreateService(
            Mock<IMailRepository> repository,
            Mock<IMessageClient> messageClient,
            Dictionary<string, string?>? configurationValues = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configurationValues ?? [])
                .Build();

            return new MailOutboxService(
                NullLogger<MailOutboxService>.Instance,
                repository.Object,
                messageClient.Object,
                configuration);
        }

        private static MailOutboxMessage CreateOutboxMessage()
        {
            return new MailOutboxMessage
            {
                ItemId = "outbox-1",
                AggregateId = "mail-1",
                MessageType = nameof(NoAttachmentSendEmailCommand),
                Destination = CommunicationConstants.NoAttachmentMailQueueName,
                PayloadJson = JsonSerializer.Serialize(new NoAttachmentSendEmailCommand
                {
                    ItemId = "mail-1",
                    Attempt = 1
                }, SerializerOptions),
                DeduplicationKey = "mail-send:mail-1:attempt:1",
                Status = OutboxMessageStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow,
                NextAttemptUtc = DateTime.UtcNow
            };
        }
    }
}
