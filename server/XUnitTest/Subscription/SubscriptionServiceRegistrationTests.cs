using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// What the running service actually gets from the container.
/// </summary>
/// <remarks>
/// A missing registration or a wrong lifetime compiles perfectly and fails on the first
/// request. Repositories in particular must stay singleton: their record of which tenants they
/// have already indexed is per instance, so a scoped one would re-issue index commands on every
/// call.
/// </remarks>
public sealed class SubscriptionServiceRegistrationTests
{
    [Fact]
    public void The_registration_returns_the_same_collection_for_chaining()
    {
        var services = new ServiceCollection();

        services.RegisterSubscriptionDomainServices(Configuration())
            .Should().BeSameAs(services);
    }

    [Theory]
    [InlineData(typeof(ISubscriptionCatalogueRepository))]
    [InlineData(typeof(IBillingAccountRepository))]
    [InlineData(typeof(ISubscriptionRepository))]
    [InlineData(typeof(ISubscriptionUsageRepository))]
    [InlineData(typeof(ISubscriptionUsageActivityRollupRepository))]
    [InlineData(typeof(ISubscriptionUsageActorRollupRepository))]
    [InlineData(typeof(ISubscriptionPaymentLinkRepository))]
    [InlineData(typeof(ISubscriptionSimulationRunRepository))]
    public void Repositories_are_singletons(Type serviceType)
    {
        var descriptor = Subscriptions()
            .Single(candidate => candidate.ServiceType == serviceType);

        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Options_bind_from_the_subscription_section()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterSubscriptionDomainServices(Configuration(new Dictionary<string, string?>
        {
            ["Subscription:EntitlementCacheSeconds"] = "42",
            ["Subscription:CounterRetentionDays"] = "7"
        }));

        var options = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<SubscriptionOptions>>()
            .Value;

        options.EntitlementCacheSeconds.Should().Be(42);
        options.CounterRetentionDays.Should().Be(7);
    }

    [Fact]
    public void Options_keep_their_defaults_when_nothing_is_configured()
    {
        var options = new SubscriptionOptions();

        options.EntitlementCacheSeconds.Should().Be(10);
        options.ReconciliationPollSeconds.Should().Be(120);
        options.TenantIds.Should().BeEmpty(
            "nothing else discovers tenants, so an omitted tenant is silently skipped by " +
            "every background sweep");
    }

    [Fact]
    public void Mail_delivery_reporter_resolves_with_the_default_system_clock()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterSubscriptionDomainServices(Configuration());

        // Replace persistence so this is a composition test rather than a MongoDB test. The
        // production failure happened while constructing the reporter, before persistence ran.
        services.AddSingleton(Mock.Of<IMailDeliveryReportRepository>());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        provider.GetRequiredService<TimeProvider>()
            .Should().BeSameAs(TimeProvider.System);
        provider.GetRequiredService<IMailDeliveryReporter>()
            .Should().BeOfType<MailDeliveryReporter>();
    }

    [Fact]
    public void Simulation_is_disabled_by_default()
    {
        new SubscriptionSimulationOptions().Enabled.Should().BeFalse(
            "the harness can rewrite billing history through real domain processors and must " +
            "never be reachable without an explicit opt-in");
    }

    [Fact]
    public void Simulation_options_bind_from_their_own_section()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterSubscriptionDomainServices(Configuration(new Dictionary<string, string?>
        {
            ["SubscriptionSimulation:Enabled"] = "true"
        }));

        var options = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<SubscriptionSimulationOptions>>()
            .Value;

        options.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Registration_refuses_to_enable_simulation_in_production()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.RegisterSubscriptionDomainServices(
            Configuration(new Dictionary<string, string?>
            {
                ["SubscriptionSimulation:Enabled"] = "true"
            }),
            new FakeHostEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>(
            "a Production deploy with the harness enabled must fail to start, not come up quietly");
    }

    [Fact]
    public void Registration_allows_simulation_enabled_outside_production()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.RegisterSubscriptionDomainServices(
            Configuration(new Dictionary<string, string?>
            {
                ["SubscriptionSimulation:Enabled"] = "true"
            }),
            new FakeHostEnvironment("Development"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Registration_refuses_to_enable_the_data_console_in_production()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.RegisterSubscriptionDomainServices(
            Configuration(new Dictionary<string, string?>
            {
                ["SubscriptionSimulation:DataConsoleEnabled"] = "true"
            }),
            new FakeHostEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>(
            "the data console reads and writes Mongo documents directly and must never come up " +
            "in Production, even with the rest of the harness left off");
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "XUnitTest";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static IServiceCollection Subscriptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterSubscriptionDomainServices(Configuration());

        return services;
    }

    private static IConfiguration Configuration(
        Dictionary<string, string?>? overrides = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(overrides ?? [])
            .Build();
}
