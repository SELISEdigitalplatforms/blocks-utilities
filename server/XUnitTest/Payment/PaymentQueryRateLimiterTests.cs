using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace XUnitTest.Payment;

/// <summary>
/// The query rate limiter runs one token bucket per tenant and a second per
/// actor, and has to fail closed when Redis is unreachable. The Lua script runs
/// server side, so the tests drive its return shape rather than its logic.
/// </summary>
public sealed class PaymentQueryRateLimiterTests
{
    private readonly List<RedisKey> _keys = [];
    private readonly Queue<RedisResult> _results = new();

    private static RedisResult TokenBucket(
        long allowed,
        long remaining,
        long retryMilliseconds) =>
        RedisResult.Create(new RedisValue[]
        {
            allowed,
            remaining,
            retryMilliseconds
        });

    private static IOptionsMonitor<PaymentOptions> Options(
        int tenantPerMinute = 600,
        int actorPerMinute = 120)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(x => x.CurrentValue).Returns(new PaymentOptions
        {
            PaymentQueryTenantRequestsPerMinute = tenantPerMinute,
            PaymentQueryActorRequestsPerMinute = actorPerMinute
        });

        return monitor.Object;
    }

    private ICacheClient Cache()
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback(
                (
                    string _,
                    RedisKey[] keys,
                    RedisValue[] _,
                    CommandFlags _) => _keys.AddRange(keys))
            .ReturnsAsync(() => _results.Count > 0
                ? _results.Dequeue()
                : TokenBucket(1, 10, 0));
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase()).Returns(database.Object);

        return cache.Object;
    }

    private PaymentQueryRateLimiter Limiter(
        ICacheClient? cache = null,
        int tenantPerMinute = 600,
        int actorPerMinute = 120) =>
        new(
            cache ?? Cache(),
            Options(tenantPerMinute, actorPerMinute),
            NullLogger<PaymentQueryRateLimiter>.Instance);

    [Fact]
    public async Task An_allowed_request_reports_the_remaining_budget()
    {
        _results.Enqueue(TokenBucket(1, 599, 0));
        _results.Enqueue(TokenBucket(1, 119, 0));

        var result = await Limiter().CheckAsync(
            "tenant-1",
            "actor-1",
            CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
        result.IsAvailable.Should().BeTrue();
        result.RetryAfterSeconds.Should().Be(0);
        result.ResetAfterSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task The_tenant_bucket_is_consumed_before_the_actor_bucket()
    {
        _results.Enqueue(TokenBucket(1, 599, 0));
        _results.Enqueue(TokenBucket(1, 119, 0));

        await Limiter().CheckAsync("tenant-1", "actor-1", CancellationToken.None);

        _keys.Should().HaveCount(2);
        _keys[0].ToString().Should().StartWith("payment:rate:query:tenant:");
        _keys[1].ToString().Should().StartWith("payment:rate:query:actor:");
    }

    [Fact]
    public async Task The_tenant_and_actor_are_hashed_rather_than_written_into_the_key()
    {
        await Limiter().CheckAsync(
            "acme-corporation",
            "ada@example.com",
            CancellationToken.None);

        _keys.Should().OnlyContain(key =>
            !key.ToString().Contains("acme", StringComparison.OrdinalIgnoreCase) &&
            !key.ToString().Contains("ada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_same_tenant_hashes_the_same_regardless_of_case_or_padding()
    {
        var limiter = Limiter();

        await limiter.CheckAsync("Tenant-1", "actor-1", CancellationToken.None);
        await limiter.CheckAsync("  tenant-1 ", "actor-1", CancellationToken.None);

        _keys[0].ToString().Should().Be(_keys[2].ToString());
    }

    [Fact]
    public async Task A_tenant_that_is_out_of_budget_short_circuits_the_actor_check()
    {
        _results.Enqueue(TokenBucket(0, 0, 4_200));

        var result = await Limiter().CheckAsync(
            "tenant-1",
            "actor-1",
            CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.RetryAfterSeconds.Should().Be(5);
        _keys.Should().ContainSingle("the actor bucket must not be charged");
    }

    [Fact]
    public async Task An_actor_that_is_out_of_budget_blocks_the_request()
    {
        _results.Enqueue(TokenBucket(1, 599, 0));
        _results.Enqueue(TokenBucket(0, 0, 900));

        var result = await Limiter().CheckAsync(
            "tenant-1",
            "actor-1",
            CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        // Sub-second retries are rounded up so a client never busy-loops.
        result.RetryAfterSeconds.Should().Be(1);
    }

    [Fact]
    public async Task The_tighter_of_the_two_budgets_is_reported()
    {
        _results.Enqueue(TokenBucket(1, 500, 0));
        _results.Enqueue(TokenBucket(1, 1, 0));

        var result = await Limiter().CheckAsync(
            "tenant-1",
            "actor-1",
            CancellationToken.None);

        result.Limit.Should().Be(120);
        result.Remaining.Should().Be(1);
    }

    [Fact]
    public async Task The_tenant_budget_is_reported_when_the_actor_has_more_headroom()
    {
        _results.Enqueue(TokenBucket(1, 2, 0));
        _results.Enqueue(TokenBucket(1, 119, 0));

        var result = await Limiter().CheckAsync(
            "tenant-1",
            "actor-1",
            CancellationToken.None);

        result.Limit.Should().Be(600);
        result.Remaining.Should().Be(2);
    }

    [Fact]
    public async Task A_negative_remaining_count_is_never_reported()
    {
        _results.Enqueue(TokenBucket(1, -5, 0));
        _results.Enqueue(TokenBucket(1, 119, 0));

        var result = await Limiter().CheckAsync(
            "tenant-1",
            "actor-1",
            CancellationToken.None);

        result.Remaining.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task A_misconfigured_limit_is_floored_at_one_request_per_minute()
    {
        _results.Enqueue(TokenBucket(1, 0, 0));
        _results.Enqueue(TokenBucket(1, 0, 0));

        var result = await Limiter(tenantPerMinute: 0, actorPerMinute: -10)
            .CheckAsync("tenant-1", "actor-1", CancellationToken.None);

        result.Limit.Should().Be(1);
    }

    [Fact]
    public async Task An_unreachable_cache_fails_closed()
    {
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase())
            .Throws(new InvalidOperationException("redis down"));

        var result = await Limiter(cache.Object).CheckAsync(
            "tenant-1",
            "actor-1",
            CancellationToken.None);

        result.IsAvailable.Should().BeFalse();
        result.IsAllowed.Should().BeFalse();
        result.RetryAfterSeconds.Should().Be(30);
    }

    [Fact]
    public async Task A_malformed_script_response_fails_closed()
    {
        _results.Enqueue(RedisResult.Create(new RedisValue[] { 1, 2 }));

        var result = await Limiter().CheckAsync(
            "tenant-1",
            "actor-1",
            CancellationToken.None);

        result.IsAvailable.Should().BeFalse();
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task A_cancelled_request_is_refused_before_touching_the_cache()
    {
        var cache = new Mock<ICacheClient>(MockBehavior.Strict);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var act = () => Limiter(cache.Object).CheckAsync(
            "tenant-1",
            "actor-1",
            source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_cancellation_raised_by_the_cache_is_not_swallowed_as_unavailable()
    {
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase())
            .Throws(new OperationCanceledException());

        var act = () => Limiter(cache.Object).CheckAsync(
            "tenant-1",
            "actor-1",
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

/// <summary>
/// The startup task migrates vault-backed provider credentials once per host
/// start. It is deliberately forgiving: a tenant that cannot be migrated must
/// not stop the service coming up for everybody else.
/// </summary>
public sealed class ProviderSecretMigrationStartupTaskTests
{
    private readonly Mock<IProviderSecretMigrationService> _migration = new();

    private static IOptionsMonitor<PaymentOptions> Options(
        bool enabled,
        params string[] tenantIds)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(x => x.CurrentValue).Returns(new PaymentOptions
        {
            MigrateProviderSecretsOnStartup = enabled,
            TenantIds = tenantIds
        });

        return monitor.Object;
    }

    private async Task RunAsync(
        IOptionsMonitor<PaymentOptions> options,
        CancellationToken cancellationToken = default)
    {
        using var task = new ProviderSecretMigrationStartupTask(
            _migration.Object,
            options,
            NullLogger<ProviderSecretMigrationStartupTask>.Instance);

        await task.StartAsync(cancellationToken);

        // Await the background work itself rather than racing StopAsync, which
        // signals shutdown before it waits.
        if (task.ExecuteTask != null)
        {
            await task.ExecuteTask;
        }

        await task.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Nothing_runs_when_the_migration_is_switched_off()
    {
        await RunAsync(Options(false, "tenant-1"));

        _migration.Verify(
            x => x.MigrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Nothing_runs_when_no_tenants_are_configured()
    {
        await RunAsync(Options(true));

        _migration.Verify(
            x => x.MigrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Every_configured_tenant_is_migrated()
    {
        _migration.Setup(x => x.MigrateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderSecretMigrationSummary(2, 1, 0));

        await RunAsync(Options(true, "tenant-1", "tenant-2"));

        _migration.Verify(
            x => x.MigrateAsync("tenant-1", It.IsAny<CancellationToken>()),
            Times.Once);
        _migration.Verify(
            x => x.MigrateAsync("tenant-2", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_tenant_with_failures_does_not_stop_the_remaining_tenants()
    {
        _migration.Setup(x => x.MigrateAsync(
                "tenant-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderSecretMigrationSummary(0, 0, 3));
        _migration.Setup(x => x.MigrateAsync(
                "tenant-2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderSecretMigrationSummary(1, 0, 0));

        await RunAsync(Options(true, "tenant-1", "tenant-2"));

        _migration.Verify(
            x => x.MigrateAsync("tenant-2", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_thrown_migration_does_not_bring_the_host_down()
    {
        _migration.Setup(x => x.MigrateAsync(
                "tenant-1",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vault unreachable"));
        _migration.Setup(x => x.MigrateAsync(
                "tenant-2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderSecretMigrationSummary(1, 0, 0));

        var act = () => RunAsync(Options(true, "tenant-1", "tenant-2"));

        await act.Should().NotThrowAsync();
        _migration.Verify(
            x => x.MigrateAsync("tenant-2", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_shutdown_during_the_run_stops_after_the_current_tenant()
    {
        using var task = new ProviderSecretMigrationStartupTask(
            _migration.Object,
            Options(true, "tenant-1", "tenant-2"),
            NullLogger<ProviderSecretMigrationStartupTask>.Instance);
        _migration.Setup(x => x.MigrateAsync(
                "tenant-1",
                It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>(
                (_, stoppingToken) =>
                {
                    // The host is shutting down while the first tenant runs.
                    task.StopAsync(CancellationToken.None);
                    stoppingToken.IsCancellationRequested.Should().BeTrue();
                })
            .ThrowsAsync(new OperationCanceledException());

        await task.StartAsync(CancellationToken.None);

        if (task.ExecuteTask != null)
        {
            await task.ExecuteTask;
        }

        _migration.Verify(
            x => x.MigrateAsync("tenant-2", It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// The hydrator routes secret resolution to whichever provider owns the secret
/// shape, and fails closed when nothing claims it.
/// </summary>
public sealed class PaymentProviderSecretHydratorRoutingTests
{
    private static PaymentProvider Provider(string providerName) => new()
    {
        ProviderName = providerName,
        TenantId = "tenant-1"
    };

    private static PaymentProviderSecretHydrator Hydrator(
        params IProviderSecretHydrator[] hydrators) =>
        new(hydrators, NullLogger<PaymentProviderSecretHydrator>.Instance);

    [Fact]
    public async Task The_claiming_hydrator_resolves_the_secrets()
    {
        var stripe = new Mock<IProviderSecretHydrator>();
        stripe.Setup(x => x.Supports("stripe")).Returns(true);
        stripe.Setup(x => x.HydrateAsync(
                It.IsAny<PaymentProvider>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var adyen = new Mock<IProviderSecretHydrator>();
        adyen.Setup(x => x.Supports(It.IsAny<string>())).Returns(false);

        var hydrated = await Hydrator(adyen.Object, stripe.Object)
            .HydrateAsync(Provider("stripe"), CancellationToken.None);

        hydrated.Should().BeTrue();
        adyen.Verify(
            x => x.HydrateAsync(
                It.IsAny<PaymentProvider>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_unclaimed_provider_fails_closed()
    {
        var hydrator = new Mock<IProviderSecretHydrator>();
        hydrator.Setup(x => x.Supports(It.IsAny<string>())).Returns(false);

        var hydrated = await Hydrator(hydrator.Object)
            .HydrateAsync(Provider("paypal"), CancellationToken.None);

        // Admitting it would put a provider with empty credentials in the cache.
        hydrated.Should().BeFalse();
    }

    [Fact]
    public async Task A_hydrator_that_cannot_resolve_reports_failure()
    {
        var stripe = new Mock<IProviderSecretHydrator>();
        stripe.Setup(x => x.Supports(It.IsAny<string>())).Returns(true);
        stripe.Setup(x => x.HydrateAsync(
                It.IsAny<PaymentProvider>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var hydrated = await Hydrator(stripe.Object)
            .HydrateAsync(Provider("stripe"), CancellationToken.None);

        hydrated.Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_provider_is_rejected()
    {
        var act = () => Hydrator().HydrateAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
