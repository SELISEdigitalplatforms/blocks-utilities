using DomainService.Configuration;
using DomainService.Configuration.Validators;
using DomainService.Shared;
using FluentAssertions;
using ConfigurationRepository = DomainService.Configuration.Services.ConfigurationRepository;
using ConfigurationService = DomainService.Configuration.Services.ConfigurationService;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class ConfigurationServiceIntegrationTests
{
    private readonly ConfigurationRepository _repository;
    private readonly ConfigurationService _service;

    public ConfigurationServiceIntegrationTests(MongoIntegrationFixture fixture)
    {
        _repository = new ConfigurationRepository(fixture.DbContextProvider);
        _service = new ConfigurationService(_repository, new ConfigurationValidator(_repository));
    }

    private static SaveConfigurationRequest ValidRequest(string name) => new()
    {
        Name = name,
        ChannelToNotify = NotifierTypes.SignalR,
        NotificationType = NotificationReceiverTypes.BroadcastReceiverType,
        EnablePersistence = true,
        NotifyMethod = "push"
    };

    [Fact]
    public async Task Save_persists_configuration_and_it_reads_back()
    {
        var name = "config-" + Guid.NewGuid().ToString("N");

        var response = await _service.SaveConfigurationAsync(ValidRequest(name));

        response.IsSuccess.Should().BeTrue();
        var stored = await _repository.GetByNameAsync(name);
        stored.Should().NotBeNull();
        stored.NotifyMethod.Should().Be("push");
    }

    [Fact]
    public async Task Save_with_empty_name_fails_validation()
    {
        var request = ValidRequest(string.Empty);

        var response = await _service.SaveConfigurationAsync(request);

        response.IsSuccess.Should().BeFalse();
        response.Errors.Should().ContainKey(nameof(SaveConfigurationRequest.Name));
    }

    [Fact]
    public async Task Get_by_id_and_gets_return_saved_configuration()
    {
        var name = "config-" + Guid.NewGuid().ToString("N");
        await _service.SaveConfigurationAsync(ValidRequest(name));
        var saved = await _repository.GetByNameAsync(name);

        // GetConfigurationRequest collides across assemblies (Notification + Storage), so
        // construct the Notification variant from its own assembly to disambiguate.
        dynamic getRequest = Activator.CreateInstance(
            typeof(ConfigurationService).Assembly.GetType(
                "DomainService.Configuration.GetConfigurationRequest")!)!;
        getRequest.ItemId = saved.ItemId;
        var byId = await _service.GetAsync(getRequest);
        ((object)byId).Should().NotBeNull();
        ((string)byId.Name).Should().Be(name);

        var gets = await _service.GetsAsync(new GetConfigurationsRequest { Page = 0, PageSize = 50 });
        gets.IsSuccess.Should().BeTrue();
        gets.Configurations.Should().Contain(c => c.ItemId == saved.ItemId);
        gets.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Delete_removes_configuration()
    {
        var name = "config-" + Guid.NewGuid().ToString("N");
        await _service.SaveConfigurationAsync(ValidRequest(name));
        var saved = await _repository.GetByNameAsync(name);

        dynamic deleteRequest = Activator.CreateInstance(
            typeof(ConfigurationService).Assembly.GetType(
                "DomainService.Configuration.DeleteConfigurationRequest")!)!;
        deleteRequest.ItemId = saved.ItemId;
        var response = await _service.DeleteAsync(deleteRequest);

        ((bool)response.IsSuccess).Should().BeTrue();
        (await _repository.GetByIdAsync(saved.ItemId)).Should().BeNull();
    }
}
