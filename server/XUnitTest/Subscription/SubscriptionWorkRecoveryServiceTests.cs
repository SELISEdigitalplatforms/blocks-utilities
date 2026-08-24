using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// What an operator may do about abandoned work, and what they may not.
/// </summary>
/// <remarks>
/// The work item id is a platform-wide identifier and the collection spans every tenant, so most of
/// what is asserted here is about who is allowed to act on what — the rest of the queue's safety
/// rests on leases, and none of those apply to a human with an id.
/// </remarks>
public sealed class SubscriptionWorkRecoveryServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OtherTenantId = "tenant-2";
    private const string WorkItemId = "work-1";

    private readonly Mock<ISubscriptionWorkQueue> _queue = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionAuditTrail> _audit = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionBackgroundWork? _stored = NewWork();

    public SubscriptionWorkRecoveryServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, "org-1", "actor-1", "user-1")));

        _queue
            .Setup(queue => queue.GetAsync(WorkItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored);

        _queue
            .Setup(queue => queue.TryRequeueAsync(
                WorkItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _queue
            .Setup(queue => queue.TryAbandonAsync(
                WorkItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Listing_answers_only_for_the_caller_s_own_tenant()
    {
        // The collection spans the platform. Without this, one tenant's operator reads every
        // tenant's failures — which names their subscriptions and their error codes.
        _queue
            .Setup(queue => queue.ListDeadLetteredAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync([NewWork()]);

        await Service().ListAsync(50, "corr-1", default);

        _queue.Verify(
            queue => queue.ListDeadLetteredAsync(50, It.IsAny<CancellationToken>(), TenantId),
            Times.Once);
    }

    [Fact]
    public async Task A_dead_letter_is_described_with_its_age_rather_than_two_timestamps()
    {
        _queue
            .Setup(queue => queue.ListDeadLetteredAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync([NewWork()]);

        var result = await Service().ListAsync(50, "corr-1", default);

        var described = result.Value!.Should().ContainSingle().Subject;

        // Due two hours ago. The number that should give somebody pause before they requeue it.
        described.AgeSeconds.Should().Be(7_200);
        described.WorkType.Should().Be("Renewal");
        described.LastErrorCode.Should().Be("provider_unreachable");
        described.SubscriptionId.Should().Be("sub-1");
    }

    [Fact]
    public async Task Requeueing_writes_once_and_reports_the_item_as_it_now_stands()
    {
        var result = await Service().RequeueAsync(WorkItemId, "provider recovered", "corr-1", default);

        result.IsSuccess.Should().BeTrue();
        _queue.Verify(
            queue => queue.TryRequeueAsync(
                WorkItemId, "provider recovered", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_decision_without_a_reason_is_refused_before_anything_is_written()
    {
        // Required rather than merely recorded: a dead letter set aside without a reason is a
        // decision nobody can review, and reviewing them is the only reason to keep them.
        var result = await Service().RequeueAsync(WorkItemId, "   ", "corr-1", default);

        result.ErrorCode.Should().Be("subscription_work_reason_required");
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _queue.Verify(
            queue => queue.TryRequeueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Another_tenant_s_work_is_not_found_rather_than_forbidden()
    {
        // Not found, deliberately: "forbidden" would confirm that an item with this id exists and
        // which tenant it belongs to, to somebody who was guessing.
        _stored = NewWork(tenantId: OtherTenantId);

        var result = await Service().RequeueAsync(WorkItemId, "curiosity", "corr-1", default);

        result.ErrorCode.Should().Be("subscription_work_not_found");
        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        _queue.Verify(
            queue => queue.TryRequeueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Work_that_is_no_longer_dead_lettered_is_a_conflict()
    {
        // Another operator got there first, or it was never dead-lettered. Either way this caller's
        // view is stale, and the write refused rather than resetting a live attempt's counters.
        _queue
            .Setup(queue => queue.TryRequeueAsync(
                WorkItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().RequeueAsync(WorkItemId, "retry please", "corr-1", default);

        result.ErrorCode.Should().Be("subscription_work_not_dead_lettered");
        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
    }

    [Fact]
    public async Task A_missing_item_is_not_found()
    {
        _stored = null;

        var result = await Service().AbandonAsync(WorkItemId, "no longer relevant", "corr-1", default);

        result.ErrorCode.Should().Be("subscription_work_not_found");
    }

    [Theory]
    [InlineData("Requeued")]
    [InlineData("Abandoned")]
    public async Task Every_decision_records_who_made_it_and_why(string stage)
    {
        var service = Service();

        var result = stage == "Requeued"
            ? await service.RequeueAsync(WorkItemId, "provider recovered", "corr-1", default)
            : await service.AbandonAsync(WorkItemId, "duplicate of a manual charge", "corr-1", default);

        result.IsSuccess.Should().BeTrue();

        // The actor is the point. A log line says work was requeued; this says who requeued it and
        // on what grounds, which is what anybody asking months later needs.
        _audit.Verify(
            trail => trail.RecordAsync(
                It.Is<SubscriptionAuditEvent>(recorded =>
                    recorded.Stage == stage &&
                    recorded.Source == "Operator" &&
                    recorded.ActorId == "actor-1" &&
                    recorded.UserId == "user-1" &&
                    recorded.Reason != null &&
                    recorded.Operation == "BackgroundWork:Renewal" &&
                    recorded.SubscriptionId == "sub-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_audit_trail_that_is_unavailable_does_not_undo_the_decision()
    {
        // The write already happened. Failing now would tell the operator their action did not, which
        // is false and invites them to repeat it.
        _audit
            .Setup(trail => trail.RecordAsync(
                It.IsAny<SubscriptionAuditEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("audit store unreachable"));

        var result = await Service().RequeueAsync(WorkItemId, "provider recovered", "corr-1", default);

        result.IsSuccess.Should().BeTrue();
    }

    private SubscriptionWorkRecoveryService Service() => new(
        _queue.Object,
        _contextResolver.Object,
        NullLogger<SubscriptionWorkRecoveryService>.Instance,
        _audit.Object,
        _time);

    private static SubscriptionBackgroundWork NewWork(string tenantId = TenantId) => new()
    {
        ItemId = WorkItemId,
        TenantId = tenantId,
        OrganizationId = "org-1",
        AggregateId = "sub-1",
        WorkType = SubscriptionWorkType.Renewal,
        WorkKey = "renewal:M20260901T000000Z",
        Status = BackgroundWorkStatus.DeadLetter,
        DueAtUtc = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc),
        UpdatedAtUtc = new DateTime(2026, 9, 3, 11, 0, 0, DateTimeKind.Utc),
        AttemptCount = 5,
        MaxAttempts = 5,
        LastErrorCode = "provider_unreachable",
        CorrelationId = "corr-original"
    };
}
