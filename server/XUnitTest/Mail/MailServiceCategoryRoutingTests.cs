using Blocks.Genesis;
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
            var messageClient = new Mock<IMessageClient>();
            var service = CreateService(messageClient, MailCategory.NoAttachment);

            await service.ProcessMailSent(CreateMail());

            messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<NoAttachmentSendEmailCommand>>(m =>
                m.ConsumerName == CommunicationConstants.NoAttachmentMailQueueName &&
                m.Payload.ItemId == "mail-1" &&
                m.Payload.MailCategory == MailCategory.NoAttachment)), Times.Once);
        }

        [Fact]
        public async Task ProcessMailSent_PublishesSmallAttachmentMailToSmallAttachmentQueue()
        {
            var messageClient = new Mock<IMessageClient>();
            var service = CreateService(messageClient, MailCategory.SmallAttachment);

            await service.ProcessMailSent(CreateMail());

            messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<SmallAttachmentSendEmailCommand>>(m =>
                m.ConsumerName == CommunicationConstants.SmallAttachmentMailQueueName &&
                m.Payload.ItemId == "mail-1" &&
                m.Payload.MailCategory == MailCategory.SmallAttachment)), Times.Once);
        }

        [Fact]
        public async Task ProcessMailSent_PublishesLargeAttachmentMailToLargeAttachmentQueue()
        {
            var messageClient = new Mock<IMessageClient>();
            var service = CreateService(messageClient, MailCategory.LargeAttachment);

            await service.ProcessMailSent(CreateMail());

            messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<LargeAttachmentSendEmailCommand>>(m =>
                m.ConsumerName == CommunicationConstants.LargeAttachmentMailQueueName &&
                m.Payload.ItemId == "mail-1" &&
                m.Payload.MailCategory == MailCategory.LargeAttachment)), Times.Once);
        }

        private static MailService CreateService(Mock<IMessageClient> messageClient, MailCategory category)
        {
            var repository = new Mock<IMailRepository>();
            repository
                .Setup(x => x.SaveMailToBeSent(It.IsAny<MailToBeSent>()))
                .ReturnsAsync(true);

            var resolver = new Mock<IMailCategoryResolver>();
            resolver
                .Setup(x => x.ResolveAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(category);

            var validator = new Mock<IValidator<MailToBeSent>>();
            validator
                .Setup(x => x.ValidateAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            return new MailService(
                validator.Object,
                messageClient.Object,
                repository.Object,
                resolver.Object);
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
