using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using MongoDB.Bson;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class MailRepositoryIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly MailRepository _repository;

    public MailRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _repository = new MailRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task SaveMailToBeSent_then_GetMailToBeSent_round_trips()
    {
        var mail = new MailToBeSent
        {
            ItemId = Guid.NewGuid().ToString(),
            Name = "welcome",
            Language = "en",
            To = ["user@example.com"]
        };

        (await _repository.SaveMailToBeSent(mail)).Should().BeTrue();
        var stored = await _repository.GetMailToBeSent(mail.ItemId);
        stored.Should().NotBeNull();
        stored.Name.Should().Be("welcome");
    }

    [Fact]
    public async Task Template_and_server_configuration_existence_checks()
    {
        var purpose = "purpose-" + Guid.NewGuid().ToString("N");
        var config = new MailServerConfiguration
        {
            ItemId = Guid.NewGuid().ToString(),
            Name = "smtp",
            Host = "localhost"
        };
        await _repository.GetCollection<MailServerConfiguration>().InsertOneAsync(config);
        await _repository.GetCollection<EmailTemplate>().InsertOneAsync(new EmailTemplate
        {
            ItemId = Guid.NewGuid().ToString(),
            Name = purpose,
            Language = "en",
            MailConfigurationId = config.ItemId
        });

        (await _repository.MailTemplateForPurposeExists(purpose, "en")).Should().BeTrue();
        (await _repository.MailTemplateForPurposeExists(purpose, "de")).Should().BeFalse();
        (await _repository.MailServerConfigurationExists(purpose, "en")).Should().BeTrue();
        (await _repository.MailServerConfigurationExists("missing-purpose", "en")).Should().BeFalse();
    }

    [Fact]
    public async Task GetEmailTemplateByPurpose_falls_back_to_name_and_language()
    {
        var purpose = "purpose-" + Guid.NewGuid().ToString("N");
        var config = new MailServerConfiguration { ItemId = Guid.NewGuid().ToString(), Host = "localhost" };
        await _repository.GetCollection<MailServerConfiguration>().InsertOneAsync(config);
        await _repository.GetCollection<EmailTemplate>().InsertOneAsync(new EmailTemplate
        {
            ItemId = Guid.NewGuid().ToString(),
            Name = purpose,
            Language = "en",
            MailConfigurationId = config.ItemId
        });

        // organizationId set: the org filter finds nothing, then falls back to name+language.
        var byOrg = await _repository.GetEmailTemplateByPurpose(purpose, "en", "org-1");
        byOrg.Should().NotBeNull();

        // organizationId empty: only the name+language filter runs.
        var byName = await _repository.GetEmailTemplateByPurpose(purpose, "en", string.Empty);
        byName.Should().NotBeNull();

        var serverConfig = await _repository.GetMailServerConfigurationByPurpose(purpose, "en", string.Empty);
        serverConfig!.ItemId.Should().Be(config.ItemId);
        (await _repository.GetMailServerConfigurationByPurpose("missing", "en", string.Empty)).Should().BeNull();
    }

    [Fact]
    public async Task GetMailServerConfigurationByTenantId_returns_a_configuration()
    {
        await _repository.GetCollection<MailServerConfiguration>().InsertOneAsync(
            new MailServerConfiguration { ItemId = Guid.NewGuid().ToString(), Host = "localhost" });

        (await _repository.GetMailServerConfigurationByTenantId("tenant-1")).Should().NotBeNull();
    }

    [Fact]
    public async Task FileExists_and_GetEmailAddressOfUsers_read_raw_collections()
    {
        var fileId = Guid.NewGuid().ToString();
        await _fixture.Collection<BsonDocument>("Files").InsertOneAsync(
            new BsonDocument { ["_id"] = fileId });
        (await _repository.FileExists(fileId)).Should().BeTrue();
        (await _repository.FileExists("no-such-file")).Should().BeFalse();

        var email = $"user-{Guid.NewGuid():N}@example.com";
        await _fixture.Collection<BsonDocument>("Users").InsertOneAsync(
            new BsonDocument { ["_id"] = Guid.NewGuid().ToString(), ["Email"] = email });
        var found = await _repository.GetEmailAdressOfUsers([email]);
        found.Should().Contain(email);
        (await _repository.GetEmailAdressOfUsers([])).Should().BeEmpty();
    }

    [Fact]
    public async Task GetMailBoxMails_filters_by_search_text()
    {
        var subject = "subject-" + Guid.NewGuid().ToString("N");
        await _repository.GetCollection<MailBoxEntity>().InsertOneAsync(new MailBoxEntity
        {
            ItemId = Guid.NewGuid().ToString(),
            MessageId = Guid.NewGuid().ToString(),
            Subject = subject,
            From = "a@example.com",
            To = "b@example.com",
            Status = MailStatus.Sent,
            Date = DateTime.UtcNow,
            IsInbound = false
        });

        var (mails, total) = await _repository.GetMailBoxMails(new GetMailBoxMails
        {
            ProjectKey = "tenant-1",
            SearchText = subject,
            PageNumber = 0,
            PageSize = 10
        });

        total.Should().Be(1);
        mails.Should().ContainSingle(m => m.Subject == subject);
    }

    [Fact]
    public async Task GetMailBoxAggregatedMails_groups_by_message_id()
    {
        var messageId = Guid.NewGuid().ToString();
        var subject = "subject-" + Guid.NewGuid().ToString("N");
        var collection = _repository.GetCollection<MailBoxEntity>();
        await collection.InsertOneAsync(new MailBoxEntity
        {
            ItemId = Guid.NewGuid().ToString(), MessageId = messageId, Subject = subject,
            From = "a@example.com", To = "b@example.com", Status = MailStatus.Sent,
            Date = DateTime.UtcNow.AddMinutes(-5), IsInbound = false
        });
        await collection.InsertOneAsync(new MailBoxEntity
        {
            ItemId = Guid.NewGuid().ToString(), MessageId = messageId, Subject = subject,
            From = "a@example.com", To = "b@example.com", Status = MailStatus.Delivered,
            Date = DateTime.UtcNow, IsInbound = false
        });

        var (mails, total) = await _repository.GetMailBoxAggregatedMails(new GetMailBoxMails
        {
            ProjectKey = "tenant-1",
            SearchText = subject,
            PageNumber = 0,
            PageSize = 10
        });

        total.Should().Be(1);
        mails.Should().ContainSingle();
        mails.Single().Timeline.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMailBoxMail_returns_latest_and_backfills_body()
    {
        var messageId = Guid.NewGuid().ToString();
        var collection = _repository.GetCollection<MailBoxEntity>();
        await collection.InsertOneAsync(new MailBoxEntity
        {
            ItemId = Guid.NewGuid().ToString(), MessageId = messageId, Subject = "s",
            Status = MailStatus.Sent, Body = "original-body", Date = DateTime.UtcNow.AddMinutes(-5)
        });
        await collection.InsertOneAsync(new MailBoxEntity
        {
            ItemId = Guid.NewGuid().ToString(), MessageId = messageId, Subject = "s",
            Status = MailStatus.Delivered, Body = string.Empty, Date = DateTime.UtcNow
        });

        var mail = await _repository.GetMailBoxMail(messageId, "tenant-1");

        mail.Should().NotBeNull();
        mail!.Body.Should().Be("original-body");
        (await _repository.GetMailBoxMail("no-such-message", "tenant-1")).Should().BeNull();
    }
}
