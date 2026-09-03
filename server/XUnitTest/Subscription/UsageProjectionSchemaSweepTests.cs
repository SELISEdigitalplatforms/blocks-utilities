using FluentAssertions;
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
/// The reconciliation sweep republishes a projection whose shape predates the current build.
/// </summary>
/// <remarks>
/// Adding a field to the projection is invisible to both version comparisons the sweep otherwise
/// relies on: neither the counter's <c>AppliedRecordCount</c> nor the subscription's
/// <c>Version</c> moves when the code that writes the document changes. So a document stored by an
/// older build looks entirely current, and without the schema comparison it keeps the old shape for
/// the life of its window — which for a never-resetting meter has no end.
/// <para>
/// That is not hypothetical: <c>QuantityScale</c> was added this way, and a plan that had already
/// widened its meter would have had its projection report whole units indefinitely.
/// </para>
/// </remarks>
public sealed class UsageProjectionSchemaSweepTests
{
    private const string TenantId = "tenant-1";
    private const string SubscriptionId = "sub-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageCurrentRepository> _current = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<IUsageProjectionPublisher> _publisher = new();

    public UsageProjectionSchemaSweepTests()
    {
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Subscription(version: 4));

        // Counters level with the projection, so nothing about usage is behind.
        _usage
            .Setup(repository => repository.GetCountersAsync(
                TenantId,
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SubscriptionUsageCounter>(StringComparer.Ordinal)
            {
                [DocumentId] = new() { ItemId = DocumentId, AppliedRecordCount = 9 }
            });

        _publisher
            .Setup(publisher => publisher.RefreshAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private static string DocumentId =>
        SubscriptionUsageCurrent.CreateId(SubscriptionId, "screening", "M2026-09");

    /// <summary>
    /// A document at the current schema, level on both versions, is left alone — otherwise the
    /// sweep would republish every projection on every pass.
    /// </summary>
    [Fact]
    public async Task A_current_document_is_not_republished()
    {
        Candidates(Document(schemaVersion: SubscriptionUsageCurrent.CurrentSchemaVersion));

        var repaired = await Reconciler().SweepTenantAsync(
            TenantId, "corr-1", CancellationToken.None);

        repaired.Should().Be(0);
        VerifyRefreshed(Times.Never());
    }

    /// <summary>
    /// A document written by an older build is republished even though both versions are level.
    /// </summary>
    [Fact]
    public async Task A_document_below_the_current_schema_is_republished()
    {
        Candidates(Document(schemaVersion: SubscriptionUsageCurrent.CurrentSchemaVersion - 1));

        var repaired = await Reconciler().SweepTenantAsync(
            TenantId, "corr-1", CancellationToken.None);

        repaired.Should().Be(1);
        VerifyRefreshed(Times.Once());
    }

    /// <summary>
    /// The decision itself costs no read: the document already proves it is behind.
    /// </summary>
    /// <remarks>
    /// Stated as a comparison rather than as "never", because the refresh that follows re-reads the
    /// subscription by design — it must describe what the subscription is now, not what the sweep
    /// saw. So the observable saving is one read against two: deciding by schema needs only the
    /// refresh's own read, where deciding by subscription version needs a read to decide and then
    /// the refresh's on top. This sweep is careful about that cost everywhere else.
    /// </remarks>
    [Fact]
    public async Task An_out_of_date_shape_costs_no_read_to_notice()
    {
        Candidates(Document(schemaVersion: SubscriptionUsageCurrent.CurrentSchemaVersion - 1));

        await Reconciler().SweepTenantAsync(TenantId, "corr-1", CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.GetByIdAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()),
            Times.Once,
            "only the refresh's own re-read, with nothing spent on deciding");

        _subscriptions.Invocations.Clear();

        // The same subscription, behind on its version instead: one read to decide, one to refresh.
        Candidates(
            Document(
                schemaVersion: SubscriptionUsageCurrent.CurrentSchemaVersion,
                subscriptionVersion: 3));

        await Reconciler().SweepTenantAsync(TenantId, "corr-1", CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.GetByIdAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// A document from a future build is not treated as stale, so an older instance during a
    /// rolling deploy cannot fight a newer one by republishing what it just wrote.
    /// </summary>
    [Fact]
    public async Task A_document_above_the_current_schema_is_left_alone()
    {
        Candidates(Document(schemaVersion: SubscriptionUsageCurrent.CurrentSchemaVersion + 1));

        var repaired = await Reconciler().SweepTenantAsync(
            TenantId, "corr-1", CancellationToken.None);

        repaired.Should().Be(0);
        VerifyRefreshed(Times.Never());
    }

    /// <summary>
    /// One refresh for a subscription whose documents are behind for two different reasons, since
    /// a refresh republishes all of that subscription's windows anyway.
    /// </summary>
    [Fact]
    public async Task A_subscription_behind_on_both_counts_is_refreshed_once()
    {
        Candidates(
            Document(schemaVersion: SubscriptionUsageCurrent.CurrentSchemaVersion - 1),
            Document(
                schemaVersion: SubscriptionUsageCurrent.CurrentSchemaVersion,
                meterKey: "storage",
                subscriptionVersion: 3));

        await Reconciler().SweepTenantAsync(TenantId, "corr-1", CancellationToken.None);

        VerifyRefreshed(Times.Once());
    }

    private void Candidates(params SubscriptionUsageCurrent[] documents) =>
        _current
            .Setup(repository => repository.ListBehindCountersAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents);

    private void VerifyRefreshed(Times times) =>
        _publisher.Verify(
            publisher => publisher.RefreshAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            times);

    private UsageProjectionReconciler Reconciler() => new(
        _subscriptions.Object,
        _current.Object,
        _usage.Object,
        _publisher.Object,
        new UsageProjectionBackfillCursors(),
        new OptionsStub(),
        NullLogger<UsageProjectionReconciler>.Instance,
        new ControlledTimeProvider(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)));

    private static SubscriptionUsageCurrent Document(
        int schemaVersion,
        string meterKey = "screening",
        long subscriptionVersion = 4) => new()
    {
        ItemId = SubscriptionUsageCurrent.CreateId(SubscriptionId, meterKey, "M2026-09"),
        TenantId = TenantId,
        OrganizationId = "org-1",
        SubscriptionId = SubscriptionId,
        MeterKey = meterKey,
        PeriodKey = "M2026-09",
        PeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        CounterVersion = 9,
        SubscriptionVersion = subscriptionVersion,
        SchemaVersion = schemaVersion
    };

    private static SubscriptionDetail Subscription(int version) => new()
    {
        ItemId = SubscriptionId,
        TenantId = TenantId,
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Active,
        Version = version,
        Plan = new PlanSnapshot { PlanId = "plan-1", Code = "pro" }
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<SubscriptionOptions, string?> listener) =>
            new Subscription();

        private sealed class Subscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
