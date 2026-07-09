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
            var outboxService = new Mock<IMailOutboxService>();
            var service = CreateService(outboxService, sendResult: MailSubmissionResult.Accepted());

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand { ItemId = "mail-1", TenantId = "tenant-a" });

            outboxService.Verify(x => x.EnqueueAsync(
                "mail-1",
                CommunicationConstants.MailSendCompletedTopicName,
                It.Is<MailSendCompletedEvent>(m =>
                    m.ItemId == "mail-1" &&
                    m.ProjectKey == "project-a" &&
                    m.TenantId == "tenant-a" &&
                    m.OrganizationId == "org-a" &&
                    m.Purpose == "welcome" &&
                    m.MailCategory == MailCategory.NoAttachment &&
                    m.IsSuccess &&
                    m.FailureReason == null &&
                    m.RecipientCount == 3 &&
                    m.AttachmentCount == 0 &&
                    m.IsTestMail),
                It.IsAny<string>(),
                It.IsAny<DateTime?>()), Times.Once);

            outboxService.Verify(x => x.EnqueueAsync(
                "mail-1",
                CommunicationConstants.MailDeliveryStatusCheckQueueName,
                It.Is<CheckMailDeliveryStatusCommand>(m => m.ItemId == "mail-1"),
                It.IsAny<string>(),
                It.IsAny<DateTime?>()), Times.Once);
        }

        [Fact]
        public async Task ProcessSendMailAsync_WhenSendReturnsFalse_PublishesFailureEvent()
        {
            var outboxService = new Mock<IMailOutboxService>();
            var service = CreateService(outboxService, sendResult: MailSubmissionResult.Failed("ProviderReturnedFalse", false));

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand { ItemId = "mail-1", TenantId = "tenant-a" });

            outboxService.Verify(x => x.EnqueueAsync(
                "mail-1",
                CommunicationConstants.MailSendCompletedTopicName,
                It.Is<MailSendCompletedEvent>(m => !m.IsSuccess && m.FailureReason == "ProviderReturnedFalse"),
                It.IsAny<string>(),
                It.IsAny<DateTime?>()), Times.Once);
            outboxService.Verify(x => x.EnqueueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CheckMailDeliveryStatusCommand>(),
                It.IsAny<string>(),
                It.IsAny<DateTime?>()), Times.Never);
        }

        [Fact]
        public async Task ProcessSendMailAsync_PublishesEventWithSavedTenantScope()
        {
            var outboxService = new Mock<IMailOutboxService>();
            var service = CreateService(outboxService, sendResult: MailSubmissionResult.Accepted(), projectKey: "project-a");

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand { ItemId = "mail-1", TenantId = "tenant-a" });

            outboxService.Verify(x => x.EnqueueAsync(
                "mail-1",
                CommunicationConstants.MailSendCompletedTopicName,
                It.Is<MailSendCompletedEvent>(message =>
                    message.ProjectKey == "project-a" &&
                    message.TenantId == "tenant-a"),
                It.IsAny<string>(),
                It.IsAny<DateTime?>()), Times.Once);
        }

        [Fact]
        public async Task ProcessSendMailAsync_WhenEventPublishFails_DoesNotThrowOrRetrySend()
        {
            var outboxService = new Mock<IMailOutboxService>();
            outboxService
                .Setup(x => x.EnqueueAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MailSendCompletedEvent>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime?>()))
                .ThrowsAsync(new InvalidOperationException("broker unavailable"));

            var smtpClient = new Mock<ISmtpClient>();
            smtpClient
                .Setup(x => x.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()))
                .ReturnsAsync(MailSubmissionResult.Accepted());

            var service = CreateService(outboxService, smtpClient: smtpClient);

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand { ItemId = "mail-1", TenantId = "tenant-a" });

            smtpClient.Verify(x => x.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()), Times.Once);
        }

        [Fact]
        public async Task ProcessSendMailAsync_WhenProviderRateLimited_RequeuesWithoutSending()
        {
            var outboxService = new Mock<IMailOutboxService>();
            var smtpClient = new Mock<ISmtpClient>();
            var service = CreateService(
                outboxService,
                smtpClient: smtpClient,
                providerRateLimitResult: MailRateLimitResult.Rejected("ProviderSenderMinute", "ProviderRateLimitExceeded", 45));

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand { ItemId = "mail-1", TenantId = "tenant-a", Attempt = 1 });

            smtpClient.Verify(x => x.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()), Times.Never);
            outboxService.Verify(x => x.EnqueueAsync(
                "mail-1",
                CommunicationConstants.NoAttachmentMailQueueName,
                It.Is<NoAttachmentSendEmailCommand>(m => m.ItemId == "mail-1" && m.Attempt == 1),
                It.Is<string>(key => key.StartsWith("mail-send-provider-rate-limit:mail-1:attempt:1")),
                It.Is<DateTime?>(nextAttemptUtc => nextAttemptUtc.HasValue)), Times.Once);
            outboxService.Verify(x => x.EnqueueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<MailSendCompletedEvent>(),
                It.IsAny<string>(),
                It.IsAny<DateTime?>()), Times.Never);
        }

        [Fact]
        public async Task ProcessSendMailAsync_WhenSesSubmissionSucceeds_DoesNotQueueExchangeTrace()
        {
            var outboxService = new Mock<IMailOutboxService>();
            var service = CreateService(
                outboxService,
                sendResult: MailSubmissionResult.Accepted(providerRequestId: "ses-message-1"),
                smtpClientType: SmtpClient.MsMailKit);

            await service.ProcessSendMailAsync(new NoAttachmentSendEmailCommand
            {
                ItemId = "mail-1",
                TenantId = "tenant-a"
            });

            outboxService.Verify(x => x.EnqueueAsync(
                It.IsAny<string>(),
                CommunicationConstants.MailDeliveryStatusCheckQueueName,
                It.IsAny<CheckMailDeliveryStatusCommand>(),
                It.IsAny<string>(),
                It.IsAny<DateTime?>()), Times.Never);
        }

        private static SendMailService CreateService(
            Mock<IMailOutboxService> outboxService,
            MailSubmissionResult? sendResult = null,
            string projectKey = "project-a",
            Mock<ISmtpClient>? smtpClient = null,
            MailRateLimitResult? providerRateLimitResult = null,
            SmtpClient smtpClientType = SmtpClient.MsGraph)
        {
            sendResult ??= MailSubmissionResult.Accepted();
            smtpClient ??= new Mock<ISmtpClient>();
            smtpClient
                .Setup(x => x.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()))
                .ReturnsAsync(sendResult);

            var repository = new Mock<IMailRepository>();
            repository
                .Setup(x => x.GetMailToBeSent("tenant-a", "mail-1"))
                .ReturnsAsync(CreateMail(projectKey, smtpClientType));
            repository
                .Setup(x => x.TryStartMailSubmissionAsync("tenant-a", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>()))
                .ReturnsAsync(true);
            repository
                .Setup(x => x.UpdateMailSubmissionAcceptedAsync(
                    "tenant-a",
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<MailRecipientDeliveryStatus>>(),
                    It.IsAny<MailSubmissionResult>()))
                .Returns(Task.CompletedTask);
            repository
                .Setup(x => x.UpdateMailSubmissionFailedAsync(
                    "tenant-a",
                    It.IsAny<string>(),
                    It.IsAny<MailSubmissionStatus>(),
                    It.IsAny<MailSubmissionResult>()))
                .Returns(Task.CompletedTask);

            var provider = new Mock<SmtpClientProvider>(Mock.Of<IServiceProvider>(), NullLogger<SmtpClientProvider>.Instance);
            provider
                .Setup(x => x.GetSmtpClient(It.IsAny<MailToBeSent>()))
                .Returns(smtpClient.Object);

            var limiter = new Mock<IMailSendConcurrencyLimiter>();
            limiter
                .Setup(x => x.AcquireAsync(It.IsAny<MailCategory>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NoopAsyncDisposable());

            var providerRateLimiter = new Mock<IMailProviderRateLimiter>();
            providerRateLimiter
                .Setup(x => x.CheckAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(providerRateLimitResult ?? MailRateLimitResult.Allowed());

            return new SendMailService(
                NullLogger<SendMailService>.Instance,
                repository.Object,
                provider.Object,
                limiter.Object,
                providerRateLimiter.Object,
                outboxService.Object,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["MailDeliveryTracking:InitialDelayInMinutes"] = "0"
                    })
                    .Build());
        }

        private static MailToBeSent CreateMail(string projectKey, SmtpClient smtpClientType)
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
                    SmtpClient = smtpClientType,
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
