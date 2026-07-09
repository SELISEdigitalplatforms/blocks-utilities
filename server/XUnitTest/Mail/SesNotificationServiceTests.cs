using System.Text.Json;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Services.DeliveryTracking;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Mail;

public class SesNotificationServiceTests
{
    [Theory]
    [InlineData("Delivery", MailStatus.Delivered)]
    [InlineData("DeliveryDelay", MailStatus.Pending)]
    [InlineData("Bounce", MailStatus.Bounced)]
    [InlineData("Reject", MailStatus.Rejected)]
    [InlineData("Rendering Failure", MailStatus.Failed)]
    [InlineData("Complaint", MailStatus.Complained)]
    [InlineData("Send", MailStatus.Pending)]
    public async Task ProcessAsync_MapsSesEventAndPublishesStatus(string eventType, MailStatus expectedStatus)
    {
        var repository = CreateRepository(claimed: true);
        var outbox = new Mock<IMailOutboxService>();
        var service = CreateService(repository, outbox, signatureValid: true);

        var result = await service.ProcessAsync(CreateEnvelope(eventType));

        Assert.Equal(SesNotificationOutcome.Processed, result.Outcome);
        repository.Verify(x => x.UpdateMailRecipientDeliveryStatusAsync(
            "tenant-a",
            "mail-1",
            "to@example.com",
            expectedStatus,
            It.IsAny<string?>(),
            It.IsAny<DateTime>()), Times.Once);
        outbox.Verify(x => x.EnqueueAsync(
            "mail-1",
            CommunicationConstants.MailDeliveryStatusChangedTopicName,
            It.Is<MailDeliveryStatusChangedEvent>(message =>
                message.TenantId == "tenant-a" &&
                message.Status == expectedStatus),
            It.IsAny<string>(),
            It.IsAny<DateTime?>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenSignatureIsInvalid_RejectsBeforeDatabaseAccess()
    {
        var repository = new Mock<IMailRepository>();
        var service = CreateService(repository, new Mock<IMailOutboxService>(), signatureValid: false);

        var result = await service.ProcessAsync(CreateEnvelope("Delivery"));

        Assert.Equal(SesNotificationOutcome.Forbidden, result.Outcome);
        repository.Verify(x => x.GetMailToBeSent(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenTopicDoesNotMatch_RejectsBeforeDatabaseAccess()
    {
        var repository = new Mock<IMailRepository>();
        var service = CreateService(repository, new Mock<IMailOutboxService>(), signatureValid: true);

        var result = await service.ProcessAsync(CreateEnvelope("Delivery", "arn:aws:sns:region:account:other"));

        Assert.Equal(SesNotificationOutcome.Forbidden, result.Outcome);
        repository.Verify(x => x.GetMailToBeSent(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenReceiptAlreadyClaimed_ReturnsDuplicate()
    {
        var repository = CreateRepository(claimed: false);
        var outbox = new Mock<IMailOutboxService>();
        var service = CreateService(repository, outbox, signatureValid: true);

        var result = await service.ProcessAsync(CreateEnvelope("Delivery"));

        Assert.Equal(SesNotificationOutcome.Duplicate, result.Outcome);
        outbox.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<MailDeliveryStatusChangedEvent>(),
            It.IsAny<string>(),
            It.IsAny<DateTime?>()), Times.Never);
    }

    private static SesNotificationService CreateService(
        Mock<IMailRepository> repository,
        Mock<IMailOutboxService> outbox,
        bool signatureValid)
    {
        var verifier = new Mock<IAmazonSnsMessageVerifier>();
        verifier.Setup(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(signatureValid);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AmazonSes:DeliveryTrackingEnabled"] = "true",
            ["AmazonSes:NotificationTopicArn"] = "arn:aws:sns:region:account:blocks-mail-delivery"
        }).Build();

        return new SesNotificationService(
            NullLogger<SesNotificationService>.Instance,
            verifier.Object,
            repository.Object,
            outbox.Object,
            Mock.Of<IHttpClientFactory>(),
            configuration);
    }

    private static Mock<IMailRepository> CreateRepository(bool claimed)
    {
        var repository = new Mock<IMailRepository>();
        repository.Setup(x => x.GetMailToBeSent("tenant-a", "mail-1"))
            .ReturnsAsync(new MailToBeSent
            {
                ItemId = "mail-1",
                TenantId = "tenant-a",
                ProjectKey = "project-a",
                OrganizationId = "org-a",
                RecipientDeliveryStatuses =
                [
                    new MailRecipientDeliveryStatus
                    {
                        Recipient = "to@example.com",
                        Status = MailStatus.Unknown
                    }
                ]
            });
        repository.Setup(x => x.TryClaimSesNotificationAsync(
                "tenant-a",
                "sns-message-1",
                "mail-1",
                It.IsAny<string>(),
                It.IsAny<DateTime>()))
            .ReturnsAsync(claimed);
        return repository;
    }

    private static string CreateEnvelope(string eventType, string? topicArn = null)
    {
        var detailName = eventType switch
        {
            "Bounce" => "bounce",
            "Complaint" => "complaint",
            "Delivery" => "delivery",
            "DeliveryDelay" => "deliveryDelay",
            "Reject" => "reject",
            "Rendering Failure" => "failure",
            _ => "send"
        };
        object detail = eventType switch
        {
            "Bounce" => new { timestamp = DateTime.UtcNow, bouncedRecipients = new[] { new { emailAddress = "to@example.com" } }, bounceType = "Permanent" },
            "Complaint" => new { timestamp = DateTime.UtcNow, complainedRecipients = new[] { new { emailAddress = "to@example.com" } }, complaintFeedbackType = "abuse" },
            "Delivery" => new { timestamp = DateTime.UtcNow, recipients = new[] { "to@example.com" } },
            _ => new { timestamp = DateTime.UtcNow }
        };
        var sesEvent = new Dictionary<string, object?>
        {
            ["eventType"] = eventType,
            ["mail"] = new
            {
                messageId = "ses-message-1",
                timestamp = DateTime.UtcNow,
                tags = new Dictionary<string, string[]>
                {
                    ["tenantId"] = ["tenant-a"],
                    ["mailItemId"] = ["mail-1"]
                }
            },
            [detailName] = detail
        };
        return JsonSerializer.Serialize(new
        {
            Type = "Notification",
            MessageId = "sns-message-1",
            TopicArn = topicArn ?? "arn:aws:sns:region:account:blocks-mail-delivery",
            Message = JsonSerializer.Serialize(sesEvent)
        });
    }
}
