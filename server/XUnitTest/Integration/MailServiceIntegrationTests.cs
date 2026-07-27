using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Moq;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class MailServiceIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly MailRepository _repository;
    private readonly Mock<IValidator<MailToBeSent>> _validator = new();
    private readonly Mock<IMessageClient> _messageClient = new();
    private readonly Mock<ISendMailService> _sendMailService = new();
    private readonly MailService _service;

    public MailServiceIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _repository = new MailRepository(fixture.DbContextProvider);
        _validator.Setup(v => v.ValidateAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _service = new MailService(
            _validator.Object, _messageClient.Object, _repository, _sendMailService.Object);
    }

    [Fact]
    public async Task ProcessMailSent_saves_and_triggers_send_when_valid()
    {
        var mail = new MailToBeSent { ItemId = Guid.NewGuid().ToString(), Name = "welcome", To = ["a@b.com"] };

        var response = await _service.ProcessMailSent(mail);

        response.IsSuccess.Should().BeTrue();
        (await _repository.GetMailToBeSent(mail.ItemId)).Should().NotBeNull();
        _sendMailService.Verify(s => s.ProcessSendMailAsync(
            It.Is<SendEmailEvent>(e => e.ItemId == mail.ItemId)), Times.Once);
    }

    [Fact]
    public async Task ProcessMailSent_returns_errors_when_validation_fails()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("To", "required") }));

        var response = await _service.ProcessMailSent(new MailToBeSent { ItemId = Guid.NewGuid().ToString() });

        response.IsSuccess.Should().BeFalse();
        response.Errors.Should().ContainKey("To");
    }

    [Fact]
    public async Task SendToQueue_forwards_to_message_client()
    {
        await _service.SendToQueueAsync("queue-1", new SendEmailEvent { ItemId = "x" });

        _messageClient.Verify(m => m.SendToConsumerAsync(
            It.Is<ConsumerMessage<SendEmailEvent>>(c => c.ConsumerName == "queue-1")), Times.Once);
    }

    [Fact]
    public async Task GetMailBoxMails_rejects_invalid_status()
    {
        var response = await _service.GetMailBoxMailsAsync(new GetMailBoxMails
        {
            ProjectKey = "tenant-1",
            Status = "not-a-real-status"
        });

        response.IsSuccess.Should().BeFalse();
        response.Errors.Should().ContainKey("Status");
    }

    [Fact]
    public async Task GetMailBoxMails_returns_aggregated_results()
    {
        var messageId = Guid.NewGuid().ToString();
        var subject = "subject-" + Guid.NewGuid().ToString("N");
        await _repository.GetCollection<MailBoxEntity>().InsertOneAsync(new MailBoxEntity
        {
            ItemId = Guid.NewGuid().ToString(), MessageId = messageId, Subject = subject,
            From = "a@b.com", To = "c@d.com", Status = MailStatus.Sent, Date = DateTime.UtcNow
        });

        var response = await _service.GetMailBoxMailsAsync(new GetMailBoxMails
        {
            ProjectKey = "tenant-1", SearchText = subject, PageNumber = 0, PageSize = 10
        });

        response.IsSuccess.Should().BeTrue();
        response.Mails.Should().ContainSingle();
    }

    [Fact]
    public async Task GetMailBoxMail_returns_found_and_not_found()
    {
        var messageId = Guid.NewGuid().ToString();
        await _repository.GetCollection<MailBoxEntity>().InsertOneAsync(new MailBoxEntity
        {
            ItemId = Guid.NewGuid().ToString(), MessageId = messageId, Subject = "s",
            Body = "body", Status = MailStatus.Sent, Date = DateTime.UtcNow
        });

        (await _service.GetMailBoxMailAsync(new GetMailBoxMail { MessageId = messageId, ProjectKey = "tenant-1" }))
            .IsSuccess.Should().BeTrue();
        (await _service.GetMailBoxMailAsync(new GetMailBoxMail { MessageId = "missing", ProjectKey = "tenant-1" }))
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessMailAsync_resolves_user_emails_and_template()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await _fixture.Collection<MongoDB.Bson.BsonDocument>("Users").InsertOneAsync(
            new MongoDB.Bson.BsonDocument { ["_id"] = Guid.NewGuid().ToString(), ["Email"] = email });

        BlocksContext.SetContext(BlocksContext.Create(
            "tenant-1", null, "user-1", true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, null));
        try
        {
            var response = await _service.ProcessMailAsync(new SendMail
            {
                To = [email],
                Purpose = "welcome",
                Language = "en"
            });

            response.IsSuccess.Should().BeTrue();
            _sendMailService.Verify(s => s.ProcessSendMailAsync(It.IsAny<SendEmailEvent>()), Times.Once);
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    [Fact]
    public async Task ProcessMailToAnyAsync_uses_raw_recipients()
    {
        BlocksContext.SetContext(BlocksContext.Create(
            "tenant-1", null, "user-1", true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, null));
        try
        {
            var response = await _service.ProcessMailToAnyAsync(new SendMailToAny
            {
                To = ["direct@example.com"],
                Purpose = "welcome",
                Language = "en",
                IsTestMail = true
            });

            response.IsSuccess.Should().BeTrue();
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }
}
