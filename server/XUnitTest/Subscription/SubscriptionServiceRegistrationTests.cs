using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Subscription.DomainService.Repositories;
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
    [InlineData(typeof(ISubscriptionPaymentLinkRepository))]
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
