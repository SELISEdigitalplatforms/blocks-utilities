using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Which tenants the background sweeps run for.
/// </summary>
/// <remarks>
/// The behaviour these protect: the roster used to be read once at startup, so a project created
/// afterwards was never swept and its renewals silently never happened until someone redeployed.
/// Projects are created at any time and can subscribe immediately, so this has to be a question
/// asked repeatedly rather than a value captured.
/// </remarks>
public sealed class SubscriptionTenantDirectoryTests
{
    private readonly Mock<ISubscriptionTenantSource> _source = new();
    private readonly SubscriptionOptions _options = new();
    private readonly ControlledTimeProvider _time = new(DateTimeOffset.Parse("2026-08-17T09:00:00Z"));

    public SubscriptionTenantDirectoryTests() =>
        _source
            .Setup(source => source.ListTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["tenant-1", "tenant-2"]);

    [Fact]
    public async Task Tenants_are_discovered_when_none_are_configured()
    {
        var tenants = await Directory().ListTenantIdsAsync(CancellationToken.None);

        tenants.Should().BeEquivalentTo(["tenant-1", "tenant-2"]);
    }

    [Fact]
    public async Task A_configured_list_overrides_discovery_entirely()
    {
        _options.TenantIds = ["pinned-tenant"];

        var tenants = await Directory().ListTenantIdsAsync(CancellationToken.None);

        tenants.Should().BeEquivalentTo(["pinned-tenant"]);
        _source.Verify(
            source => source.ListTenantIdsAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "the override exists for the case where discovery itself is the problem");
    }

    [Fact]
    public async Task The_roster_is_reused_within_the_refresh_window()
    {
        var directory = Directory();

        await directory.ListTenantIdsAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(_options.TenantRefreshSeconds - 1));
        await directory.ListTenantIdsAsync(CancellationToken.None);

        _source.Verify(
            source => source.ListTenantIdsAsync(It.IsAny<CancellationToken>()),
            Times.Once,
            "the sweep runs far more often than the roster changes");
    }

    [Fact]
    public async Task A_tenant_created_after_startup_is_picked_up_on_the_next_refresh()
    {
        var directory = Directory();

        await directory.ListTenantIdsAsync(CancellationToken.None);

        _source
            .Setup(source => source.ListTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["tenant-1", "tenant-2", "tenant-new"]);
        _time.Advance(TimeSpan.FromSeconds(_options.TenantRefreshSeconds));

        var tenants = await directory.ListTenantIdsAsync(CancellationToken.None);

        tenants.Should().Contain("tenant-new");
    }

    [Fact]
    public async Task A_failed_read_keeps_the_last_known_roster()
    {
        var directory = Directory();

        await directory.ListTenantIdsAsync(CancellationToken.None);

        _source
            .Setup(source => source.ListTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("registry unreachable"));
        _time.Advance(TimeSpan.FromSeconds(_options.TenantRefreshSeconds));

        var tenants = await directory.ListTenantIdsAsync(CancellationToken.None);

        tenants.Should().BeEquivalentTo(
            ["tenant-1", "tenant-2"],
            "sweeping nothing because the registry blinked would stop billing while the " +
            "service went on looking healthy");
    }

    [Fact]
    public async Task A_failed_first_read_is_empty_rather_than_fatal()
    {
        _source
            .Setup(source => source.ListTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("registry unreachable"));

        var directory = Directory();

        var tenants = await directory.ListTenantIdsAsync(CancellationToken.None);

        tenants.Should().BeEmpty();

        // And the next attempt still tries: a failure must not poison the cache into never
        // reading again, which is how the old startup-once behaviour failed.
        _source
            .Setup(source => source.ListTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["tenant-1"]);
        _time.Advance(TimeSpan.FromSeconds(_options.TenantRefreshSeconds));

        (await directory.ListTenantIdsAsync(CancellationToken.None))
            .Should().BeEquivalentTo(["tenant-1"]);
    }

    [Fact]
    public async Task An_empty_registry_is_a_quiet_pass_and_is_retried()
    {
        _source
            .Setup(source => source.ListTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var directory = Directory();

        (await directory.ListTenantIdsAsync(CancellationToken.None)).Should().BeEmpty();

        _source
            .Setup(source => source.ListTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["tenant-first"]);
        _time.Advance(TimeSpan.FromSeconds(_options.TenantRefreshSeconds));

        (await directory.ListTenantIdsAsync(CancellationToken.None))
            .Should().BeEquivalentTo(
                ["tenant-first"],
                "an empty registry is legitimate on a fresh environment, so it cannot be the " +
                "end of discovery");
    }

    private SubscriptionTenantDirectory Directory() => new(
        _source.Object,
        new StaticOptionsMonitor(_options),
        NullLogger<SubscriptionTenantDirectory>.Instance,
        _time);

    private sealed class StaticOptionsMonitor : IOptionsMonitor<SubscriptionOptions>
    {
        private readonly SubscriptionOptions _value;

        public StaticOptionsMonitor(SubscriptionOptions value) => _value = value;

        public SubscriptionOptions CurrentValue => _value;

        public SubscriptionOptions Get(string? name) => _value;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
