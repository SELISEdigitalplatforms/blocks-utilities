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
    public class MailDeliveryStatusServiceTests
    {
        [Theory]
        [InlineData("Delivered", MailStatus.Delivered)]
        [InlineData("Failed", MailStatus.Failed)]
        [InlineData("Pending", MailStatus.Pending)]
        [InlineData("Quarantined", MailStatus.Quarantined)]
        [InlineData("Filtered as spam", MailStatus.Quarantined)]
        [InlineData("SomethingElse", MailStatus.Unknown)]
        public void Map_ReturnsExpectedMailStatus(string exchangeStatus, MailStatus expectedStatus)
        {
            Assert.Equal(expectedStatus, MailDeliveryStatusMapper.Map(exchangeStatus));
        }

        [Fact]
        public async Task ProcessDeliveryStatusCheckAsync_PublishesProjectScopedDeliveryEvent()
        {
            var repository = new Mock<IMailRepository>();
            repository
                .Setup(x => x.GetMailToBeSent("mail-1"))
                .ReturnsAsync(CreateMail());
            repository
                .Setup(x => x.UpdateMailRecipientDeliveryStatusAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MailStatus>(),
                    It.IsAny<string?>(),
                    It.IsAny<DateTime>()))
                .Returns(Task.CompletedTask);

            var traceClient = new Mock<IExchangeMessageTraceClient>();
            traceClient
                .Setup(x => x.GetDeliveryStatusesAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new ExchangeMessageTraceResult
                    {
                        Recipient = "to@example.com",
                        Status = MailStatus.Delivered,
                        StatusReason = "250 2.1.5 Recipient OK"
                    }
                ]);

            var outboxService = new Mock<IMailOutboxService>();
            var service = CreateService(repository, traceClient, outboxService);

            await service.ProcessDeliveryStatusCheckAsync(new CheckMailDeliveryStatusCommand { ItemId = "mail-1" });

            repository.Verify(x => x.UpdateMailRecipientDeliveryStatusAsync(
                "mail-1",
                "to@example.com",
                MailStatus.Delivered,
                "250 2.1.5 Recipient OK",
                It.IsAny<DateTime>()), Times.Once);

            outboxService.Verify(x => x.EnqueueAsync(
                "mail-1",
                CommunicationConstants.GetMailDeliveryStatusChangedQueueName("project-a"),
                It.Is<MailDeliveryStatusChangedEvent>(m =>
                    m.ItemId == "mail-1" &&
                    m.ProjectKey == "project-a" &&
                    m.TenantId == "tenant-a" &&
                    m.OrganizationId == "org-a" &&
                    m.Recipient == "to@example.com" &&
                    m.Status == MailStatus.Delivered &&
                    m.StatusReason == "250 2.1.5 Recipient OK"),
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
        public async Task ProcessDeliveryStatusCheckAsync_Requeues_WhenStatusIsPendingAndAttemptsRemain()
        {
            var repository = CreateRepository();
            var traceClient = CreateTraceClient(MailStatus.Pending);
            var outboxService = new Mock<IMailOutboxService>();
            var service = CreateService(repository, traceClient, outboxService);

            await service.ProcessDeliveryStatusCheckAsync(new CheckMailDeliveryStatusCommand { ItemId = "mail-1", Attempt = 1 });

            outboxService.Verify(x => x.EnqueueAsync(
                "mail-1",
                CommunicationConstants.MailDeliveryStatusCheckQueueName,
                It.Is<CheckMailDeliveryStatusCommand>(m =>
                    m.ItemId == "mail-1" &&
                    m.Attempt == 2 &&
                    m.NotBeforeUtc > DateTime.UtcNow),
                It.IsAny<string>(),
                It.IsAny<DateTime?>()), Times.Once);
        }

        [Fact]
        public async Task ProcessDeliveryStatusCheckAsync_DoesNotRequeue_WhenMaxAttemptsReached()
        {
            var repository = CreateRepository();
            var traceClient = CreateTraceClient(MailStatus.Unknown);
            var outboxService = new Mock<IMailOutboxService>();
            var service = CreateService(repository, traceClient, outboxService);

            await service.ProcessDeliveryStatusCheckAsync(new CheckMailDeliveryStatusCommand { ItemId = "mail-1", Attempt = 2 });

            outboxService.Verify(x => x.EnqueueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CheckMailDeliveryStatusCommand>(),
                It.IsAny<string>(),
                It.IsAny<DateTime?>()), Times.Never);
        }

        private static MailDeliveryStatusService CreateService(
            Mock<IMailRepository> repository,
            Mock<IExchangeMessageTraceClient> traceClient,
            Mock<IMailOutboxService> outboxService)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MailDeliveryTracking:MaxAttempts"] = "2",
                    ["MailDeliveryTracking:RetryDelayMinutes:0"] = "1"
                })
                .Build();

            return new MailDeliveryStatusService(
                NullLogger<MailDeliveryStatusService>.Instance,
                repository.Object,
                traceClient.Object,
                outboxService.Object,
                configuration);
        }

        private static Mock<IMailRepository> CreateRepository()
        {
            var repository = new Mock<IMailRepository>();
            repository
                .Setup(x => x.GetMailToBeSent("mail-1"))
                .ReturnsAsync(CreateMail());
            repository
                .Setup(x => x.UpdateMailRecipientDeliveryStatusAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MailStatus>(),
                    It.IsAny<string?>(),
                    It.IsAny<DateTime>()))
                .Returns(Task.CompletedTask);

            return repository;
        }

        private static Mock<IExchangeMessageTraceClient> CreateTraceClient(MailStatus status)
        {
            var traceClient = new Mock<IExchangeMessageTraceClient>();
            traceClient
                .Setup(x => x.GetDeliveryStatusesAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new ExchangeMessageTraceResult
                    {
                        Recipient = "to@example.com",
                        Status = status,
                        StatusReason = status.ToString()
                    }
                ]);

            return traceClient;
        }

        private static MailToBeSent CreateMail()
        {
            return new MailToBeSent
            {
                ItemId = "mail-1",
                ProjectKey = "project-a",
                TenantId = "tenant-a",
                OrganizationId = "org-a",
                To = ["to@example.com"],
                Cc = [],
                Bcc = [],
                ReplyTo = [],
                Attachments = []
            };
        }
    }
}
