using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// The backfill's place in the roster, across the scopes it actually runs in.
/// </summary>
/// <remarks>
/// The reconciler is scoped and the reconciliation background service opens a fresh scope per tenant
/// sweep, so a cursor held as a field on the reconciler was a new empty dictionary on every pass: the
/// backfill re-read page one forever and no tenant larger than one page ever had its later pages
/// published. These tests resolve the reconciler from real DI scopes rather than constructing it, so
/// that lifetime mistake fails here instead of in production.
/// </remarks>
public sealed class UsageProjectionBackfillTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageCurrentRepository> _current = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<IUsageProjectionPublisher> _publisher = new();
    private readonly List<string?> _requestedCursors = [];

    public UsageProjectionBackfillTests()
    {
        _current
            .Setup(repository => repository.ListBehindCountersAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _publisher
            .Setup(publisher => publisher.RefreshAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Two full pages then a short one, recording the cursor each pass asked with.
        _subscriptions
            .Setup(repository => repository.ListLivePageAsync(
                TenantId,
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? afterId, int limit, CancellationToken _) =>
            {
                _requestedCursors.Add(afterId);

                var start = afterId is null ? 0 : int.Parse(afterId["sub-".Length..]) + 1;

                if (start >= 5)
                {
                    return [];
                }

                return Enumerable
                    .Range(start, Math.Min(limit, 5 - start))
                    .Select(index => Subscription($"sub-{index}"))
                    .ToList();
            });
    }

    /// <summary>
    /// The bug, stated as a test: a second pass in a second scope must resume after the first page.
    /// </summary>
    [Fact]
    public async Task A_second_pass_in_a_new_scope_resumes_after_the_first_page()
    {
        using var provider = Provider(pageSize: 2);

        await BackfillOnceAsync(provider);
        await BackfillOnceAsync(provider);

        _requestedCursors.Should().HaveCount(2);
        _requestedCursors[0].Should().BeNull("the first pass starts at the beginning");
        _requestedCursors[1].Should().Be(
            "sub-1", "the second pass must continue where the first stopped, not restart");
    }

    /// <summary>
    /// Every subscription is reached, which is the property that matters rather than the cursor
    /// itself: a consumer reading the collection directly has no fallback for a missing document.
    /// </summary>
    [Fact]
    public async Task Successive_passes_cover_every_live_subscription_exactly_once()
    {
        using var provider = Provider(pageSize: 2);

        var refreshed = new List<string>();

        _publisher
            .Setup(publisher => publisher.RefreshAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail subscription, DateTime _, string _, CancellationToken _) =>
            {
                refreshed.Add(subscription.ItemId);

                return 1;
            });

        for (var pass = 0; pass < 3; pass++)
        {
            await BackfillOnceAsync(provider);
        }

        refreshed.Should().Equal("sub-0", "sub-1", "sub-2", "sub-3", "sub-4");
    }

    /// <summary>
    /// Reaching the end starts again, because this is a cycle rather than a migration: a meter added
    /// to a plan tomorrow is a missing document tomorrow.
    /// </summary>
    [Fact]
    public async Task Exhausting_the_roster_starts_the_next_pass_from_the_beginning()
    {
        using var provider = Provider(pageSize: 10);

        // First pass takes all five and reports a short page, so the position is cleared.
        var first = await BackfillOnceAsync(provider);
        first.LastSubscriptionId.Should().BeNull();

        await BackfillOnceAsync(provider);

        _requestedCursors.Should().Equal(null, null);
    }

    /// <summary>
    /// The store is the thing that has to outlive the scope, so its registration is asserted rather
    /// than assumed. Registered scoped, it is a different instance per pass and the cursor is lost.
    /// </summary>
    [Fact]
    public void The_cursor_store_is_a_singleton_so_it_survives_the_reconcilers_scope()
    {
        using var provider = Provider(pageSize: 2);

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var storeInFirst = first.ServiceProvider.GetRequiredService<UsageProjectionBackfillCursors>();
        var storeInSecond = second.ServiceProvider.GetRequiredService<UsageProjectionBackfillCursors>();

        storeInSecond.Should().BeSameAs(storeInFirst);

        first.ServiceProvider.GetRequiredService<IUsageProjectionReconciler>()
            .Should().NotBeSameAs(
                second.ServiceProvider.GetRequiredService<IUsageProjectionReconciler>(),
                "the reconciler itself is per-scope, which is why the cursor cannot live on it");
    }

    /// <summary>Runs one pass in its own scope, the way the reconciliation service does.</summary>
    private static async Task<UsageProjectionBackfillResult> BackfillOnceAsync(
        ServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IUsageProjectionReconciler>()
            .BackfillTenantAsync(TenantId, "corr-1", CancellationToken.None);
    }

    private ServiceProvider Provider(int pageSize)
    {
        var services = new ServiceCollection();

        services.AddSingleton(_subscriptions.Object);
        services.AddSingleton(_current.Object);
        services.AddSingleton(_usage.Object);
        services.AddSingleton(_publisher.Object);
        services.AddSingleton<IOptionsMonitor<SubscriptionOptions>>(
            new OptionsStub(pageSize));
        services.AddSingleton<ILogger<UsageProjectionReconciler>>(
            NullLogger<UsageProjectionReconciler>.Instance);
        services.AddSingleton<TimeProvider>(
            new ControlledTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)));

        // The two registrations under test, exactly as the module registers them.
        services.AddScoped<IUsageProjectionReconciler, UsageProjectionReconciler>();
        services.AddSingleton<UsageProjectionBackfillCursors>();

        return services.BuildServiceProvider();
    }

    private static SubscriptionDetail Subscription(string itemId) => new()
    {
        ItemId = itemId,
        TenantId = TenantId,
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Active,
        Plan = new PlanSnapshot { PlanId = "plan-1", Code = "pro" }
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public OptionsStub(int pageSize) =>
            CurrentValue = new SubscriptionOptions
            {
                UsageProjectionBackfillBatchSize = pageSize
            };

        public SubscriptionOptions CurrentValue { get; }

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
