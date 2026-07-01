using FluentValidation;
using FluentValidation.Results;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Moq;

namespace XUnitTest.Mail
{
    public class MailServiceCategoryRoutingTests
    {
        [Fact]
        public async Task ProcessMailSent_PublishesNoAttachmentMailToNoAttachmentQueue()
        {
            var (service, repository) = CreateService(MailCategory.NoAttachment);

            await service.ProcessMailSent(CreateMail());

            repository.Verify(x => x.SaveMailToBeSentWithOutboxAsync(
                It.IsAny<MailToBeSent>(),
                It.Is<MailOutboxMessage>(m =>
                    m.Destination == CommunicationConstants.NoAttachmentMailQueueName &&
                    m.MessageType == nameof(NoAttachmentSendEmailCommand))), Times.Once);
        }

        [Fact]
        public async Task ProcessMailSent_PublishesSmallAttachmentMailToSmallAttachmentQueue()
        {
            var (service, repository) = CreateService(MailCategory.SmallAttachment);

            await service.ProcessMailSent(CreateMail());

            repository.Verify(x => x.SaveMailToBeSentWithOutboxAsync(
                It.IsAny<MailToBeSent>(),
                It.Is<MailOutboxMessage>(m =>
                    m.Destination == CommunicationConstants.SmallAttachmentMailQueueName &&
                    m.MessageType == nameof(SmallAttachmentSendEmailCommand))), Times.Once);
        }

        [Fact]
        public async Task ProcessMailSent_PublishesLargeAttachmentMailToLargeAttachmentQueue()
        {
            var (service, repository) = CreateService(MailCategory.LargeAttachment);

            await service.ProcessMailSent(CreateMail());

            repository.Verify(x => x.SaveMailToBeSentWithOutboxAsync(
                It.IsAny<MailToBeSent>(),
                It.Is<MailOutboxMessage>(m =>
                    m.Destination == CommunicationConstants.LargeAttachmentMailQueueName &&
                    m.MessageType == nameof(LargeAttachmentSendEmailCommand))), Times.Once);
        }

        private static (MailService Service, Mock<IMailRepository> Repository) CreateService(MailCategory category)
        {
            var repository = new Mock<IMailRepository>();
            repository
                .Setup(x => x.SaveMailToBeSentWithOutboxAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailOutboxMessage>()))
                .ReturnsAsync(true);

            var resolver = new Mock<IMailCategoryResolver>();
            resolver
                .Setup(x => x.ResolveAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(category);

            var validator = new Mock<IValidator<MailToBeSent>>();
            validator
                .Setup(x => x.ValidateAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            var outboxService = new Mock<IMailOutboxService>();
            SetupCreateMessage(outboxService);

            var service = new MailService(
                validator.Object,
                repository.Object,
                resolver.Object,
                outboxService.Object);

            return (service, repository);
        }

        private static void SetupCreateMessage(Mock<IMailOutboxService> outboxService)
        {
            outboxService
                .Setup(x => x.CreateMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NoAttachmentSendEmailCommand>(), It.IsAny<string>(), It.IsAny<DateTime?>()))
                .Returns<string, string, NoAttachmentSendEmailCommand, string, DateTime?>((aggregateId, destination, payload, deduplicationKey, nextAttemptUtc) =>
                    CreateOutboxMessage(aggregateId, destination, nameof(NoAttachmentSendEmailCommand), deduplicationKey));

            outboxService
                .Setup(x => x.CreateMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SmallAttachmentSendEmailCommand>(), It.IsAny<string>(), It.IsAny<DateTime?>()))
                .Returns<string, string, SmallAttachmentSendEmailCommand, string, DateTime?>((aggregateId, destination, payload, deduplicationKey, nextAttemptUtc) =>
                    CreateOutboxMessage(aggregateId, destination, nameof(SmallAttachmentSendEmailCommand), deduplicationKey));

            outboxService
                .Setup(x => x.CreateMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LargeAttachmentSendEmailCommand>(), It.IsAny<string>(), It.IsAny<DateTime?>()))
                .Returns<string, string, LargeAttachmentSendEmailCommand, string, DateTime?>((aggregateId, destination, payload, deduplicationKey, nextAttemptUtc) =>
                    CreateOutboxMessage(aggregateId, destination, nameof(LargeAttachmentSendEmailCommand), deduplicationKey));
        }

        private static MailOutboxMessage CreateOutboxMessage(string aggregateId, string destination, string messageType, string deduplicationKey)
        {
            return new MailOutboxMessage
            {
                ItemId = Guid.NewGuid().ToString(),
                AggregateId = aggregateId,
                Destination = destination,
                MessageType = messageType,
                DeduplicationKey = deduplicationKey
            };
        }

        private static MailToBeSent CreateMail()
        {
            return new MailToBeSent
            {
                ItemId = "mail-1",
                Attachments = [],
                To = ["to@example.com"],
                Cc = [],
                Bcc = [],
                ReplyTo = []
            };
        }
    }
}
