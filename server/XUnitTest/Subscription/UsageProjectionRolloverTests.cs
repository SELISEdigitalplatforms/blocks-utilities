using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Scheduling;

namespace XUnitTest.Subscription;

/// <summary>
/// A new usage window getting its zero-usage documents the moment it opens.
/// </summary>
/// <remarks>
/// This is the guarantee a direct consumer depends on and cannot work around. The API falls back to
/// the counters when the projection cannot answer; something reading the collection over Mongo has no
/// fallback, so at one minute past midnight on a new period it would see either nothing for a
/// periodic meter or — worse, because it looks like an answer — only the never-resetting ones.
/// <para>
/// Two mistakes were made here before, and both are pinned below. The refresh was first gated on the
/// queue item naming a subscription, which nothing does — every closure item comes from the repair
/// sweep — so it never ran. It was then driven by a second due query, which is not the set the closure
/// actually closed.
/// </para>
/// </remarks>
public sealed class UsageProjectionRolloverTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionUsageRatingProcessor> _rating = new();
    private readonly Mock<IUsageProjectionReconciler> _projections = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();

    public UsageProjectionRolloverTests() => Closes("sub-1", "sub-2");

    /// <summary>
    /// The refresh set comes from the closure's own committed outcome.
    /// </summary>
    /// <remarks>
    /// Not from a second due query. That query has its own batch size and its own <c>now</c>, and by
    /// the time it runs the clocks have advanced — so it could name subscriptions that were not closed
    /// and miss ones that were.
    /// </remarks>
    [Fact]
    public async Task Exactly_the_subscriptions_the_closure_rolled_are_refreshed()
    {
        await Handler().ExecuteAsync(Work(), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshManyAsync(
                TenantId,
                It.Is<IReadOnlyList<string>>(ids =>
                    ids.Count == 2 && ids.Contains("sub-1") && ids.Contains("sub-2")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// A subscription the closure did not roll is not refreshed: its window did not move, so nothing
    /// about its current projection changed. That covers the one deferred by an outstanding usage
    /// claim, which rating skips and picks up on a later pass — and which a second due query would
    /// have named.
    /// </summary>
    [Fact]
    public async Task A_due_subscription_that_closed_nothing_is_not_refreshed()
    {
        await Handler().ExecuteAsync(Work(), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshManyAsync(
                It.IsAny<string>(),
                It.Is<IReadOnlyList<string>>(ids => !ids.Contains("sub-deferred")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The queue only ever delivers a tenant-wide closure item, so the refresh has to happen on that
    /// path. Gated on the aggregate id, as it once was, this never ran at all.
    /// </summary>
    [Fact]
    public async Task A_tenant_wide_item_with_no_aggregate_id_still_publishes()
    {
        await Handler().ExecuteAsync(Work(aggregateId: ""), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshManyAsync(
                TenantId,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>Published after the closure, so the window it resolves is the new one.</summary>
    [Fact]
    public async Task The_new_windows_are_published_after_the_closure_commits()
    {
        var order = new List<string>();

        _rating
            .Setup(processor => processor.CloseDuePeriodsAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("close"))
            .ReturnsAsync(new UsagePeriodClosureOutcome(1, ["sub-1"]));

        _projections
            .Setup(reconciler => reconciler.RefreshManyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("publish"))
            .ReturnsAsync(1);

        await Handler().ExecuteAsync(Work(), CancellationToken.None);

        order.Should().Equal(["close", "publish"]);
    }

    /// <summary>
    /// A projection failure must not decide whether authoritative rating work is done.
    /// </summary>
    /// <remarks>
    /// The closure has committed by the time the refresh runs. Letting the failure out would retry
    /// the closure item, so a derived read model would be controlling whether a rating pass counts as
    /// complete.
    /// <para>
    /// The earlier version of this test asserted the exception <em>was</em> thrown, under a name
    /// saying it was not — it documented the bug as though it were the design.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_failed_projection_refresh_does_not_fail_the_closure_item()
    {
        FailTheRefresh();

        var outcome = await Handler().ExecuteAsync(Work(), CancellationToken.None);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        outcome.ErrorCode.Should().BeNull();
    }

    /// <summary>And the projection is not simply abandoned: a repair is announced per subscription.</summary>
    [Fact]
    public async Task A_failed_projection_refresh_schedules_a_repair_for_each_rolled_subscription()
    {
        FailTheRefresh();

        await Handler().ExecuteAsync(Work(), CancellationToken.None);

        foreach (var subscriptionId in new[] { "sub-1", "sub-2" })
        {
            _scheduler.Verify(
                scheduler => scheduler.ScheduleUsageProjectionRefreshAsync(
                    TenantId,
                    It.IsAny<string>(),
                    subscriptionId,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    /// <summary>
    /// Cancellation is the worker shutting down, not a projection problem, so it propagates and the
    /// item is left to be reclaimed rather than reported complete.
    /// </summary>
    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        _projections
            .Setup(reconciler => reconciler.RefreshManyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await Handler().ExecuteAsync(Work(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// A subscription named on the item is refreshed even when the closure did not roll it, and is
    /// not listed twice when it did.
    /// </summary>
    [Fact]
    public async Task A_named_subscription_is_added_once()
    {
        await Handler().ExecuteAsync(Work(aggregateId: "sub-9"), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshManyAsync(
                TenantId,
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 3 && ids.Contains("sub-9")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _projections.Invocations.Clear();

        await Handler().ExecuteAsync(Work(aggregateId: "sub-1"), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshManyAsync(
                TenantId,
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 2),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "sub-1 was already rolled, and one document must not be written twice");
    }

    /// <summary>
    /// Nothing rolled is the ordinary case — the sweep announces closure on a timer — so it must not
    /// cost a projection write.
    /// </summary>
    [Fact]
    public async Task Nothing_rolled_publishes_nothing()
    {
        Closes();

        await Handler().ExecuteAsync(Work(aggregateId: ""), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshManyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void Closes(params string[] rolledSubscriptionIds) =>
        _rating
            .Setup(processor => processor.CloseDuePeriodsAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsagePeriodClosureOutcome(
                rolledSubscriptionIds.Length,
                rolledSubscriptionIds));

    private void FailTheRefresh() =>
        _projections
            .Setup(reconciler => reconciler.RefreshManyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the projection write failed"));

    private UsagePeriodClosureWorkHandler Handler() =>
        new(
            _rating.Object,
            _projections.Object,
            _scheduler.Object,
            NullLogger<UsagePeriodClosureWorkHandler>.Instance);

    private static SubscriptionBackgroundWork Work(string aggregateId = "") => new()
    {
        ItemId = "work-1",
        TenantId = TenantId,
        OrganizationId = "org-1",
        AggregateId = aggregateId,
        WorkType = SubscriptionWorkType.UsagePeriodClosure,
        CorrelationId = "corr-1"
    };
}
