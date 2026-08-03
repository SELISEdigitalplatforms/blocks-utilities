using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Template.Models;
using Mail.DomainService.Template.Services;
using Mail.DomainService.Template.Validators;
using Microsoft.Extensions.Logging;
using Moq;
using CloneTemplateRequest = Mail.DomainService.Template.CloneTemplateRequest;
using DeleteTemplateRequest = Mail.DomainService.Template.DeleteTemplateRequest;
using TemplateModel = Mail.DomainService.Template.Template;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class TemplateServiceIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly TemplateRepository _repository;
    private readonly Mock<IHttpService> _httpService = new();
    private readonly TemplateService _service;

    public TemplateServiceIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _repository = new TemplateRepository(fixture.DbContextProvider);
        _service = new TemplateService(
            new TemplateValidator(_repository),
            _repository,
            new Mock<ILogger<TemplateService>>().Object,
            _httpService.Object);
    }

    private static TemplateModel NewTemplate(string name) => new()
    {
        Name = name,
        Language = "en",
        TemplateBody = "body",
        TemplateSubject = "subject",
        MailConfigurationId = "config-1"
    };

    [Fact]
    public async Task SaveTemplate_creates_new_template()
    {
        var name = "tpl-" + Guid.NewGuid().ToString("N");

        var response = await _service.SaveTemplateAsync(NewTemplate(name));

        response.IsSuccess.Should().BeTrue();
        response.ItemId.Should().NotBeNullOrEmpty();
        (await _repository.GetByNameAndLanguageAsync(name, "en")).Should().NotBeNull();
    }

    [Fact]
    public async Task SaveTemplate_rejects_duplicate_name_and_language()
    {
        var name = "tpl-" + Guid.NewGuid().ToString("N");
        await _service.SaveTemplateAsync(NewTemplate(name));

        var duplicate = await _service.SaveTemplateAsync(NewTemplate(name));

        duplicate.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SaveTemplate_updates_existing_by_item_id()
    {
        var name = "tpl-" + Guid.NewGuid().ToString("N");
        var created = await _service.SaveTemplateAsync(NewTemplate(name));

        var update = NewTemplate(name);
        update.ItemId = created.ItemId;
        update.TemplateSubject = "updated-subject";
        var response = await _service.SaveTemplateAsync(update);

        response.IsSuccess.Should().BeTrue();
        (await _repository.GetByIdAsync(created.ItemId!))!.TemplateSubject.Should().Be("updated-subject");
    }

    [Fact]
    public async Task GetAll_and_Get_return_templates()
    {
        var name = "tpl-" + Guid.NewGuid().ToString("N");
        var created = await _service.SaveTemplateAsync(NewTemplate(name));

        var all = await _service.GetAllTemplatesAsync(new GetAllTemplates
        {
            PageNumber = 0, PageSize = 50, SearchKey = name
        });
        all.Templates.Should().ContainSingle(t => t.Name == name);

        var single = await _service.GetAsync(new GetTemplate { ItemId = created.ItemId });
        single!.Name.Should().Be(name);
    }

    [Fact]
    public async Task Clone_copies_template_with_new_name()
    {
        var name = "tpl-" + Guid.NewGuid().ToString("N");
        var created = await _service.SaveTemplateAsync(NewTemplate(name));

        var clone = await _service.CloneTemplateAsync(new CloneTemplateRequest
        {
            ItemId = created.ItemId,
            Name = name + "-copy"
        });

        clone.IsSuccess.Should().BeTrue();
        (await _repository.GetByIdAsync(clone.ItemId!))!.Name.Should().Be(name + "-copy");
    }

    [Fact]
    public async Task Clone_missing_template_fails()
    {
        var clone = await _service.CloneTemplateAsync(new CloneTemplateRequest { ItemId = Guid.NewGuid().ToString() });
        clone.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_removes_existing_and_reports_missing()
    {
        var created = await _service.SaveTemplateAsync(NewTemplate("tpl-" + Guid.NewGuid().ToString("N")));

        (await _service.DeleteAsync(new DeleteTemplateRequest { ItemId = created.ItemId! }))
            .IsSuccess.Should().BeTrue();
        (await _service.DeleteAsync(new DeleteTemplateRequest { ItemId = Guid.NewGuid().ToString() }))
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetTemplatePluginToken_returns_token_for_json_payload()
    {
        var provider = "provider-" + Guid.NewGuid().ToString("N");
        await _fixture.Collection<TemplatePluginConfig>("TemplatePluginConfigs").InsertOneAsync(
            new TemplatePluginConfig
            {
                ItemId = Guid.NewGuid().ToString(),
                PluginProvider = provider,
                HttpMethod = "POST",
                RequestUri = "https://example.com/login",
                ContentType = "application/json",
                Payload = "{\"uid\":\"placeholder\"}",
                HttpHeders = new Dictionary<string, string> { ["Authorization"] = "drop", ["X-Api"] = "keep" }
            });
        _httpService.Setup(h => h.SendRequest<BeeLoginResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((new BeeLoginResponse { AccessToken = "token-123" }, string.Empty));

        var response = await _service.GetTemplatePluginTokenAsync(provider, "user-1");

        response!.AccessToken.Should().Be("token-123");
    }

    [Fact]
    public async Task GetTemplatePluginToken_handles_form_payload_and_null_response()
    {
        var provider = "provider-" + Guid.NewGuid().ToString("N");
        await _fixture.Collection<TemplatePluginConfig>("TemplatePluginConfigs").InsertOneAsync(
            new TemplatePluginConfig
            {
                ItemId = Guid.NewGuid().ToString(),
                PluginProvider = provider,
                HttpMethod = "POST",
                RequestUri = "https://example.com/login",
                ContentType = "application/x-www-form-urlencoded",
                Payload = "{\"uid\":\"placeholder\"}"
            });
        _httpService.Setup(h => h.SendRequest<BeeLoginResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync(((BeeLoginResponse)null!, "boom"));

        (await _service.GetTemplatePluginTokenAsync(provider, "user-1")).Should().BeNull();
    }

    [Fact]
    public async Task GetTemplatePluginToken_returns_null_when_uid_missing()
    {
        (await _service.GetTemplatePluginTokenAsync("any", string.Empty)).Should().BeNull();
    }
}
