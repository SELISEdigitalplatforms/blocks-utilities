using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Template.Services;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class TemplateRepositoryIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly TemplateRepository _repository;

    public TemplateRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _repository = new TemplateRepository(fixture.DbContextProvider);
    }

    private static EmailTemplate NewTemplate(string name, string language = "en") => new()
    {
        ItemId = Guid.NewGuid().ToString(),
        Name = name,
        Language = language,
        TemplateSubject = "subject",
        TemplateBody = "body",
        MailConfigurationId = "config-1"
    };

    [Fact]
    public async Task Save_then_get_by_id_and_name_language()
    {
        var name = "tpl-" + Guid.NewGuid().ToString("N");
        var template = NewTemplate(name);

        await _repository.SaveAsync(template);

        (await _repository.GetByIdAsync(template.ItemId))!.Name.Should().Be(name);
        (await _repository.GetByNameAndLanguageAsync(name, "en"))!.ItemId.Should().Be(template.ItemId);
        (await _repository.GetByNameAndLanguageAsync(name, "fr")).Should().BeNull();
    }

    [Fact]
    public async Task Save_upserts_existing_template()
    {
        var template = NewTemplate("tpl-" + Guid.NewGuid().ToString("N"));
        await _repository.SaveAsync(template);
        template.TemplateSubject = "updated-subject";

        await _repository.SaveAsync(template);

        (await _repository.GetByIdAsync(template.ItemId))!.TemplateSubject.Should().Be("updated-subject");
    }

    [Fact]
    public async Task Gets_filters_by_search_key_and_language()
    {
        var name = "searchable-" + Guid.NewGuid().ToString("N");
        await _repository.SaveAsync(NewTemplate(name));

        var response = await _repository.GetsAsync(new GetAllTemplates
        {
            PageNumber = 0,
            PageSize = 50,
            SearchKey = name,
            Language = "en"
        });

        response.TotalCount.Should().Be(1);
        response.Templates.Should().ContainSingle(t => t.Name == name);
    }

    [Fact]
    public async Task Delete_removes_template()
    {
        var template = NewTemplate("tpl-" + Guid.NewGuid().ToString("N"));
        await _repository.SaveAsync(template);

        await _repository.DeleteAsync(template.ItemId);

        (await _repository.GetByIdAsync(template.ItemId)).Should().BeNull();
    }

    [Fact]
    public async Task GetPluginConfig_matches_provider_case_insensitively()
    {
        var provider = "Provider-" + Guid.NewGuid().ToString("N");

        // No matching document yet: exercises the empty-result path.
        (await _repository.GetPluginConfigAsync(provider)).Should().BeNull();

        await _fixture.Collection<TemplatePluginConfig>("TemplatePluginConfigs").InsertOneAsync(
            new TemplatePluginConfig
            {
                ItemId = Guid.NewGuid().ToString(),
                PluginProvider = provider,
                RequestUri = "https://example.com"
            });

        var matched = await _repository.GetPluginConfigAsync(provider.ToLowerInvariant());
        matched.Should().NotBeNull();
        matched!.PluginProvider.Should().Be(provider);
    }
}
