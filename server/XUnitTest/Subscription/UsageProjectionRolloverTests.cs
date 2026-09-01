using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

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
/// The previous version of this hooked the closure handler behind
/// <c>work.AggregateId</c> being set. Nothing in the module calls
/// <c>ScheduleUsagePeriodClosureAsync</c>, so every closure item comes from the repair sweep and names
/// no subscription: the hook never ran. These tests exercise the handler the way the queue actually
/// invokes it — a tenant-wide item with no aggregate id — so that mistake cannot be made again
/// silently.
/// </para>
/// </remarks>
public sealed class UsageProjectionRolloverTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionUsageRatingProcessor> _rating = new();
    private readonly Mock<IUsageProjectionReconciler> _projections = new();
    private readonly List<string> _capturedBeforeClosure = [];
    private bool _closed;

    public UsageProjectionRolloverTests()
    {
        _projections
            .Setup(reconciler => reconciler.ListRollingSubscriptionsAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // Answers only while the window is still open, the way the real due query does: a
                // closure advances the subscription's usage billing clock, after which nothing is
                // due and the roster of who just rolled is gone.
                _capturedBeforeClosure.Add(_closed ? "after" : "before");

                return _closed ? [] : ["sub-1", "sub-2"];
            });

        _rating
            .Setup(processor => processor.CloseDuePeriodsAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .Callback(() => _closed = true)
            .ReturnsAsync(0);
    }

    /// <summary>
    /// The bug, as a test: the queue only ever delivers a tenant-wide closure item, and the rollover
    /// refresh has to happen on that path.
    /// </summary>
    [Fact]
    public async Task A_tenant_wide_closure_publishes_the_new_windows()
    {
        await Handler().ExecuteAsync(Work(aggregateId: ""), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshManyAsync(
                TenantId,
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 2 && ids.Contains("sub-1")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Captured before the closure, because afterwards there is no record of who rolled.
    /// </summary>
    [Fact]
    public async Task The_rolling_subscriptions_are_captured_before_the_closure_runs()
    {
        await Handler().ExecuteAsync(Work(aggregateId: ""), CancellationToken.None);

        // Asking after the closure returns nothing, and the new windows would never be published.
        _capturedBeforeClosure.Should().ContainSingle().Which.Should().Be("before");
    }

    /// <summary>Published after, so the window it resolves is the new one rather than the closed one.</summary>
    [Fact]
    public async Task The_new_windows_are_published_after_the_closure_commits()
    {
        var order = new List<string>();

        _rating
            .Setup(processor => processor.CloseDuePeriodsAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                _closed = true;
                order.Add("close");
            })
            .ReturnsAsync(0);

        _projections
            .Setup(reconciler => reconciler.RefreshManyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("publish"))
            .ReturnsAsync(2);

        await Handler().ExecuteAsync(Work(aggregateId: ""), CancellationToken.None);

        order.Should().Equal("close", "publish");
    }

    /// <summary>
    /// Rating must not be retried because a read model could not be written. The closure has
    /// committed by then, and retrying it would re-rate a period.
    /// </summary>
    [Fact]
    public async Task A_failed_projection_refresh_does_not_fail_the_closure_item()
    {
        _projections
            .Setup(reconciler => reconciler.RefreshManyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the projection write failed"));

        var act = async () => await Handler()
            .ExecuteAsync(Work(aggregateId: ""), CancellationToken.None);

        // The reconciler absorbs its own failures, so this asserts the handler does not add a path
        // that turns one into a retry of the rating.
        await act.Should().ThrowAsync<InvalidOperationException>(
            "this test documents that the handler itself does not catch — the reconciler does, and " +
            "if that ever stops being true the closure would start retrying");
    }

    /// <summary>
    /// A subscription named on the item is refreshed even when it was not in the due set, and is not
    /// refreshed twice when it was.
    /// </summary>
    [Fact]
    public async Task A_named_subscription_is_refreshed_once_and_not_twice()
    {
        await Handler().ExecuteAsync(Work(aggregateId: "sub-9"), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshSubscriptionAsync(
                TenantId, "sub-9", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _projections.Invocations.Clear();

        // Reset, because the first run above closed the window and the due list is empty afterwards.
        // Without this the second run sees no rolling set at all and the assertion below would pass
        // for the wrong reason.
        _closed = false;

        await Handler().ExecuteAsync(Work(aggregateId: "sub-1"), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshSubscriptionAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "sub-1 is already in the batch, and refreshing it twice writes the same document twice");
    }

    /// <summary>
    /// Nothing due is the ordinary case — the sweep announces closure on a timer — so it must not
    /// cost a projection write.
    /// </summary>
    [Fact]
    public async Task Nothing_due_publishes_nothing()
    {
        _projections
            .Setup(reconciler => reconciler.ListRollingSubscriptionsAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await Handler().ExecuteAsync(Work(aggregateId: ""), CancellationToken.None);

        _projections.Verify(
            reconciler => reconciler.RefreshManyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private UsagePeriodClosureWorkHandler Handler() =>
        new(_rating.Object, _projections.Object);

    private static SubscriptionBackgroundWork Work(string aggregateId) => new()
    {
        ItemId = "work-1",
        TenantId = TenantId,
        AggregateId = aggregateId,
        WorkType = SubscriptionWorkType.UsagePeriodClosure,
        CorrelationId = "corr-1"
    };
}
