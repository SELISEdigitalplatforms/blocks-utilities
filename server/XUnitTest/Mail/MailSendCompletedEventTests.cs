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
    public class MailSendCompletedEventTests
    {
        [Fact]
        public async Task ProcessSendMailAsync_WhenSendSucceeds_PublishesSuccessEventToProjectScopedDestination()
        {
            var messageClient = new Mock<IMessageClient>();
            var service = CreateService(messageClient, sendResult: true);

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand { ItemId = "mail-1" });

            messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<MailSendCompletedEvent>>(m =>
                m.ConsumerName == CommunicationConstants.GetMailSendCompletedQueueName("project-a") &&
                m.Payload.ItemId == "mail-1" &&
                m.Payload.ProjectKey == "project-a" &&
                m.Payload.TenantId == "tenant-a" &&
                m.Payload.Purpose == "welcome" &&
                m.Payload.MailCategory == MailCategory.NoAttachment &&
                m.Payload.IsSuccess &&
                m.Payload.FailureReason == null &&
                m.Payload.RecipientCount == 3 &&
                m.Payload.AttachmentCount == 0 &&
                m.Payload.IsTestMail)), Times.Once);

            messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<CheckMailDeliveryStatusCommand>>(m =>
                m.ConsumerName == CommunicationConstants.MailDeliveryStatusCheckQueueName &&
                m.Payload.ItemId == "mail-1")), Times.Once);
        }

        [Fact]
        public async Task ProcessSendMailAsync_WhenSendReturnsFalse_PublishesFailureEvent()
        {
            var messageClient = new Mock<IMessageClient>();
            var service = CreateService(messageClient, sendResult: false);

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand { ItemId = "mail-1" });

            messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<MailSendCompletedEvent>>(m =>
                m.ConsumerName == CommunicationConstants.GetMailSendCompletedQueueName("project-a") &&
                !m.Payload.IsSuccess &&
                m.Payload.FailureReason == "ProviderReturnedFalse")), Times.Once);
            messageClient.Verify(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<CheckMailDeliveryStatusCommand>>()), Times.Never);
        }

        [Fact]
        public async Task ProcessSendMailAsync_PublishesOnlyToSavedProjectDestination()
        {
            var messageClient = new Mock<IMessageClient>();
            var service = CreateService(messageClient, sendResult: true, projectKey: "project-a");

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand { ItemId = "mail-1" });

            messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<MailSendCompletedEvent>>(m =>
                m.ConsumerName == CommunicationConstants.GetMailSendCompletedQueueName("project-a"))), Times.Once);
            messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<MailSendCompletedEvent>>(m =>
                m.ConsumerName == CommunicationConstants.GetMailSendCompletedQueueName("project-b"))), Times.Never);
        }

        [Fact]
        public async Task ProcessSendMailAsync_WhenEventPublishFails_DoesNotThrowOrRetrySend()
        {
            var messageClient = new Mock<IMessageClient>();
            messageClient
                .Setup(x => x.SendToConsumerAsync(It.IsAny<ConsumerMessage<MailSendCompletedEvent>>()))
                .ThrowsAsync(new InvalidOperationException("broker unavailable"));

            var smtpClient = new Mock<ISmtpClient>();
            smtpClient
                .Setup(x => x.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()))
                .ReturnsAsync(true);

            var service = CreateService(messageClient, smtpClient: smtpClient);

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand { ItemId = "mail-1" });

            smtpClient.Verify(x => x.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()), Times.Once);
        }

        private static SendMailService CreateService(
            Mock<IMessageClient> messageClient,
            bool sendResult = true,
            string projectKey = "project-a",
            Mock<ISmtpClient>? smtpClient = null)
        {
            smtpClient ??= new Mock<ISmtpClient>();
            smtpClient
                .Setup(x => x.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()))
                .ReturnsAsync(sendResult);

            var repository = new Mock<IMailRepository>();
            repository
                .Setup(x => x.GetMailToBeSent("mail-1"))
                .ReturnsAsync(CreateMail(projectKey));
            repository
                .Setup(x => x.UpdateMailSubmissionTrackingAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<MailRecipientDeliveryStatus>>()))
                .Returns(Task.CompletedTask);

            var provider = new Mock<SmtpClientProvider>(Mock.Of<IServiceProvider>(), NullLogger<SmtpClientProvider>.Instance);
            provider
                .Setup(x => x.GetSmtpClient(It.IsAny<MailToBeSent>()))
                .Returns(smtpClient.Object);

            var limiter = new Mock<IMailSendConcurrencyLimiter>();
            limiter
                .Setup(x => x.AcquireAsync(It.IsAny<MailCategory>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NoopAsyncDisposable());

            return new SendMailService(
                NullLogger<SendMailService>.Instance,
                repository.Object,
                provider.Object,
                limiter.Object,
                messageClient.Object,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["MailDeliveryTracking:InitialDelayInMinutes"] = "0"
                    })
                    .Build());
        }

        private static MailToBeSent CreateMail(string projectKey)
        {
            return new MailToBeSent
            {
                ItemId = "mail-1",
                Name = "welcome",
                ProjectKey = projectKey,
                TenantId = "tenant-a",
                OrganizationId = "org-a",
                MailCategory = MailCategory.NoAttachment,
                To = ["to@example.com"],
                Cc = ["cc@example.com"],
                Bcc = ["bcc@example.com"],
                ReplyTo = [],
                Attachments = [],
                IsTestMail = true,
                SubjectDataContext = [],
                BodyDataContext = [],
                EmailTemplate = new EmailTemplate
                {
                    Name = "welcome-template",
                    TemplateSubject = "Welcome",
                    TemplateBody = "Hello"
                },
                MailServerConfiguration = new MailServerConfiguration
                {
                    SmtpClient = SmtpClient.Default,
                    SenderAddress = "sender@example.com"
                }
            };
        }

        private sealed class NoopAsyncDisposable : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
