using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
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
/// Ending a subscription, and how little that changes today.
/// </summary>
public sealed class SubscriptionCancellationServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionPaymentLinkRepository> _links = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly Mock<IUsagePeriodClosureRepository> _closures = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail? _subscription = NewSubscription();
    private SubscriptionTransition? _transition;

    public SubscriptionCancellationServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, transition, _) => _transition = transition)
            .ReturnsAsync(true);
    }

    /// <summary>
    /// The boundary of the change that made "no subscription" a 200 on <c>GET /current</c>.
    /// </summary>
    /// <remarks>
    /// That read asks whether there is a subscription, and "no" is one of its two ordinary answers.
    /// This is a request <em>about</em> one, and a subscription that does not exist really is
    /// something absent: answering 200 would let a caller believe it had cancelled something.
    /// </remarks>
    [Fact]
    public async Task Cancelling_a_subscription_that_does_not_exist_is_still_not_found()
    {
        _subscription = null;

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should().Be("subscription_not_found");
    }

    [Fact]
    public async Task Cancelling_keeps_the_period_that_was_paid_for()
    {
        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Active,
            "taking access away on the day someone cancels is charging for a month and " +
            "delivering part of one");
        _transition.CancelAtPeriodEnd.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Active));
    }

    [Fact]
    public async Task Cancelling_stops_the_next_payment()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _transition!.ClearNextFeeBillingAt.Should().BeTrue();
        _transition.CanceledAtUtc.Should().Be(
            new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task When_it_was_asked_for_is_separate_from_when_it_takes_effect()
    {
        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.Value!.CanceledAtUtc.Should().NotBeNull();
        _transition!.EndedAtUtc.Should().BeNull(
            "it has not ended yet, and conflating the two loses the answer to most support " +
            "questions about cancellation");
    }

    [Fact]
    public async Task An_immediate_cancellation_ends_it_now()
    {
        var result = await Service().CancelAsync(
            "sub-1", immediately: true, "fraud", null, "corr-1", CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.Canceled);
        _transition.EndedAtUtc.Should().NotBeNull();
        _transition.CancellationReason.Should().Be("fraud");
        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Canceled));
    }

    [Fact]
    public async Task An_immediate_cancellation_stops_the_usage_rating_sweep()
    {
        await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-1", CancellationToken.None);

        _transition!.ClearNextUsageBillingAt.Should().BeTrue(
            "nothing more will be metered once entitlement stops immediately");
    }

    [Fact]
    public async Task An_immediate_cancellation_queues_its_final_usage_window_for_rating()
    {
        await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-1", CancellationToken.None);

        _transition!.OutgoingUsagePeriod.Should().NotBeNull(
            "stopping the usage clock must not also forfeit whatever overage the still-open " +
            "final window already accrued");
        _transition.OutgoingUsagePeriod!.PeriodStartUtc.Should().Be(
            _subscription!.CurrentUsagePeriodStartUtc);
        _transition.OutgoingUsagePeriod.PeriodEndUtc.Should().Be(
            _time.GetUtcNow().UtcDateTime,
            "entitlement stopped at the instant of the request, not wherever the window's own " +
            "natural end happened to fall — an invoice through the later end would claim " +
            "service the subscriber never had");
    }

    [Fact]
    public async Task An_immediate_cancellation_midway_through_a_usage_window_cuts_it_short()
    {
        _subscription!.CurrentUsagePeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        _subscription.CurrentUsagePeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-1", CancellationToken.None);

        _transition!.OutgoingUsagePeriod!.PeriodStartUtc.Should().Be(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        _transition.OutgoingUsagePeriod.PeriodEndUtc.Should().Be(
            _time.GetUtcNow().UtcDateTime,
            "August 1 - September 1 cut short at the cancellation instant, not run to September 1");
    }

    [Fact]
    public async Task Abandoning_an_incomplete_checkout_ends_it_even_with_the_default_flag()
    {
        _subscription!.Status = SubscriptionStatus.Incomplete;

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, "checkout abandoned", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Incomplete);
        _transition.NewStatus.Should().Be(SubscriptionStatus.Canceled,
            "an unpaid checkout has no paid period whose end can be awaited");
        _transition.CancelAtPeriodEnd.Should().BeFalse();
        _transition.EndedAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime);
        _transition.ClearNextFeeBillingAt.Should().BeTrue();
        _transition.ClearNextUsageBillingAt.Should().BeTrue();
        _transition.Event!.EventType.Should().Be(SubscriptionConstants.SubscriptionCanceled);
        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Canceled));
    }

    [Fact]
    public async Task An_at_period_end_cancellation_leaves_usage_rating_untouched()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _transition!.ClearNextUsageBillingAt.Should().BeFalse(
            "the subscription keeps granting and metering until the period actually ends");
    }

    [Fact]
    public async Task Cancelling_drops_the_cached_entitlement_immediately()
    {
        await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-1", CancellationToken.None);

        _cache.Verify(
            cache => cache.Invalidate(TenantId, OrganizationId),
            Times.Once,
            "the cached snapshot decides what the customer may do");
    }

    [Fact]
    public async Task Cancelling_raises_an_event()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _transition!.Event!.EventType.Should()
            .Be(SubscriptionConstants.SubscriptionCancellationRequested);
        _transition.Event.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task Another_organizations_subscription_reports_as_missing()
    {
        _subscription = null;

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound,
            "a forbidden response would confirm the identifier exists somewhere else");
    }

    [Fact]
    public async Task Cancelling_an_ended_subscription_is_a_conflict()
    {
        _subscription!.Status = SubscriptionStatus.Canceled;

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_already_ended");
    }

    [Fact]
    public async Task Losing_the_transition_race_is_reported_as_a_conflict()
    {
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        _cache.Verify(
            cache => cache.Invalidate(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task A_trialing_subscription_can_be_cancelled_too()
    {
        _subscription!.Status = SubscriptionStatus.Trialing;

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Trialing);
    }

    [Fact]
    public async Task A_requested_organization_is_forwarded_to_context_resolution()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, "org-9", "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    /// <summary>
    /// The tab is still open. Someone can finish a card form after cancelling, and the provider
    /// will duly report a stored card against a subscription that no longer wants one.
    /// </summary>
    [Fact]
    public async Task Cancelling_before_activation_closes_the_attempt_still_waiting_on_the_provider()
    {
        _subscription!.Status = SubscriptionStatus.Incomplete;
        _links
            .Setup(repository => repository.FindBySubscriptionAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionPaymentLink
            {
                ItemId = "link-1",
                TenantId = TenantId,
                SubscriptionId = "sub-1",
                Purpose = SubscriptionPaymentPurpose.PaymentMethodSetup,
                State = SubscriptionPaymentLinkState.Pending
            });

        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _links.Verify(
            repository => repository.TrySettleAsync(
                TenantId,
                "link-1",
                SubscriptionPaymentLinkState.Abandoned,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cancelling_a_live_subscription_leaves_its_payment_links_alone()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _links.Verify(
            repository => repository.TrySettleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionPaymentLinkState>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "a renewal's link belongs to a charge this cancellation has nothing to say about");
    }

    [Fact]
    public async Task An_ordinary_cancellation_may_later_be_escalated()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _transition!.CanCancelImmediately.Should().BeTrue();
        ApplyLastTransition();

        var result = await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-2", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Canceled);
        _transition.CancelAtPeriodEnd.Should().BeFalse();
        _transition.CanCancelImmediately.Should().BeFalse(
            "a cancellation that has already taken effect cannot itself be escalated again");
        result.Value!.Cancellation!.State.Should().Be("Effective");
        result.Value.Cancellation.CanCancelImmediately.Should().BeFalse();
        result.Value.CancelAtPeriodEnd.Should().BeFalse();
        _transition.OutgoingUsagePeriod.Should().NotBeNull(
            "escalating a schedule also stops the usage clock right now, so the window still " +
            "open at that moment must be queued for rating exactly as a fresh immediate " +
            "cancellation queues its own");
        _transition.OutgoingUsagePeriod!.PeriodEndUtc.Should().Be(_time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Escalating_midway_through_a_usage_window_cuts_it_short_at_the_escalation_instant()
    {
        _subscription!.CurrentUsagePeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        _subscription.CurrentUsagePeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);
        ApplyLastTransition();

        _time.Advance(TimeSpan.FromDays(6)); // schedule requested Aug 14, escalated Aug 20.

        await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-2", CancellationToken.None);

        _transition!.OutgoingUsagePeriod!.PeriodStartUtc.Should().Be(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        _transition.OutgoingUsagePeriod.PeriodEndUtc.Should().Be(
            new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
            "the window is cut at the escalation instant, not run to its own September 1 end");
    }

    [Fact]
    public async Task Scheduling_a_cancellation_also_schedules_targeted_work_for_the_promised_boundary()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _scheduler.Verify(
            scheduler => scheduler.ScheduleCancellationEffectiveAsync(
                It.Is<SubscriptionDetail>(subscription => subscription.ItemId == "sub-1"),
                _subscription!.CurrentPeriodEndUtc,
                "corr-1",
                It.IsAny<CancellationToken>()),
            Times.Once,
            "so this cancellation is finished close to its boundary rather than only whenever " +
            "the tenant repair sweep next happens to pass over this subscription");
    }

    [Fact]
    public async Task Escalating_or_ending_immediately_does_not_schedule_targeted_cancellation_work()
    {
        await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-1", CancellationToken.None);

        _scheduler.Verify(
            scheduler => scheduler.ScheduleCancellationEffectiveAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "entitlement already stopped now — there is no future boundary left to finish");
    }

    [Fact]
    public async Task A_targeted_scheduling_failure_does_not_fail_the_cancellation_itself()
    {
        _scheduler
            .Setup(scheduler => scheduler.ScheduleCancellationEffectiveAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("work queue unavailable"));

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "the schedule was already recorded durably; the targeted item is best effort, and " +
            "the tenant repair sweep remains the guaranteed path if it is lost");
    }

    [Fact]
    public async Task An_immediate_cancellation_reserves_and_commits_closing_its_usage_period()
    {
        _subscription!.Plan.Meters = [new PlanMeter { MeterKey = "screening", UnitLabel = "screening" }];

        await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-1", CancellationToken.None);

        _closures.Verify(
            closures => closures.TryReserveClosingAsync(
                TenantId, "sub-1", It.IsAny<string>(), _time.GetUtcNow().UtcDateTime,
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "no new usage claim must be granted against this period once entitlement has " +
            "stopped");
        _closures.Verify(
            closures => closures.TryCommitClosingAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the transition that stopped entitlement succeeded, so the reservation is committed " +
            "rather than left short of Closing");
    }

    [Fact]
    public async Task Abandoning_an_incomplete_checkout_never_reserves_a_usage_closure()
    {
        _subscription!.Status = SubscriptionStatus.Incomplete;
        _subscription.Plan.Meters = [new PlanMeter { MeterKey = "screening", UnitLabel = "screening" }];

        await Service().CancelAsync(
            "sub-1", immediately: false, "checkout abandoned", null, "corr-1", CancellationToken.None);

        _closures.Verify(
            closures => closures.TryReserveClosingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an abandoned checkout never activated — there is no usage window it could have " +
            "opened, whatever the plan it never got to use defines");
    }

    [Fact]
    public async Task Scheduling_a_cancellation_does_not_reserve_the_usage_period()
    {
        _subscription!.Plan.Meters = [new PlanMeter { MeterKey = "screening", UnitLabel = "screening" }];

        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _closures.Verify(
            closures => closures.TryReserveClosingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "usage keeps accruing normally through a period that is only scheduled to end, not " +
            "yet ended");
    }

    [Fact]
    public async Task A_cancellation_that_loses_its_transition_releases_the_reservation()
    {
        _subscription!.Plan.Meters = [new PlanMeter { MeterKey = "screening", UnitLabel = "screening" }];
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-1", CancellationToken.None);

        _closures.Verify(
            closures => closures.TryReleaseReservationAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "a reservation for a cancellation that never actually happened must not go on " +
            "refusing ordinary usage");
        _closures.Verify(
            closures => closures.TryCommitClosingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task The_write_that_first_schedules_a_cancellation_requires_none_be_scheduled_yet()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _transition!.RequireCancellationNotAlreadyScheduled.Should().BeTrue(
            "status alone does not move for this write, so it cannot arbitrate two concurrent " +
            "first-time requests the way every status-changing transition can");
    }

    [Fact]
    public async Task A_cancellation_locked_to_a_prepaid_annual_term_cannot_be_escalated()
    {
        _subscription!.PendingAnnualPeriod = new PendingAnnualPeriod
        {
            StartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrepaid = true
        };

        var first = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);
        first.Value!.Cancellation!.CanCancelImmediately.Should().BeFalse(
            "escalating this one would forfeit a year already paid for");
        ApplyLastTransition();

        var escalation = await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-2", CancellationToken.None);

        escalation.IsSuccess.Should().BeTrue();
        escalation.Value!.Status.Should().Be(nameof(SubscriptionStatus.Active),
            "the schedule is honoured as far as it safely can be, not forfeited");
        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.Is<SubscriptionTransition>(t => t.NewStatus == SubscriptionStatus.Canceled),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "a request this schedule cannot grant must not write anything");
    }

    [Fact]
    public async Task Repeating_a_period_end_cancellation_writes_nothing()
    {
        var first = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);
        ApplyLastTransition();
        _subscription!.Version = 7;

        var repeat = await Service().CancelAsync(
            "sub-1", immediately: false, "different reason", null, "corr-2", CancellationToken.None);

        repeat.IsSuccess.Should().BeTrue();
        repeat.Value!.Cancellation!.RequestedAtUtc
            .Should().Be(first.Value!.Cancellation!.RequestedAtUtc);
        repeat.Value.Version.Should().Be(7, "no new transition means no version bump");
        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "only the original request should ever have written a transition");
    }

    [Fact]
    public async Task A_lost_compare_and_set_that_converges_on_the_same_schedule_succeeds()
    {
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Another request scheduled the exact same cancellation first — the losing caller should
        // see that as success rather than a conflict it did not actually run into.
        var winner = NewSubscription();
        winner.CancelAtPeriodEnd = true;
        winner.CanCancelImmediately = true;
        winner.CanceledAtUtc = _time.GetUtcNow().UtcDateTime;

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription)
            .Callback(() => _subscription = winner);

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Cancellation!.State.Should().Be("Scheduled");
    }

    [Fact]
    public async Task A_lost_compare_and_set_against_a_genuinely_different_state_is_still_a_conflict()
    {
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // The subscription moved for an unrelated reason — a plan change, say — not because
        // another cancellation request already got what this one wanted.
        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
    }

    [Fact]
    public async Task A_stale_reservation_for_a_subscription_that_actually_canceled_at_the_boundary_commits()
    {
        var boundary = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var closure = NewStaleClosure(boundary);
        var canceled = NewSubscription();
        canceled.Status = SubscriptionStatus.Canceled;
        canceled.EndedAtUtc = boundary;
        canceled.CancelAtPeriodEnd = false;

        _closures
            .Setup(closures => closures.ListStaleReservationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([closure]);
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(canceled);

        var reconciled = await Service().ReconcileStaleClosuresAsync(TenantId, CancellationToken.None);

        reconciled.Should().Be(1);
        _closures.Verify(
            closures => closures.TryCommitClosingAsync(
                TenantId, "sub-1", "M20260801T000000Z", closure.CloseOperationId!,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the cancellation this reservation belonged to actually took effect at the recorded " +
            "boundary, so the reservation left short of Closing must be finished, not undone");
        _closures.Verify(
            closures => closures.TryReleaseReservationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_stale_reservation_for_a_subscription_that_is_still_live_releases_back_to_open()
    {
        var boundary = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var closure = NewStaleClosure(boundary);
        var stillLive = NewSubscription();
        stillLive.Status = SubscriptionStatus.Active;
        stillLive.CancelAtPeriodEnd = false;
        stillLive.CurrentPeriodEndUtc = boundary.AddDays(30);

        _closures
            .Setup(closures => closures.ListStaleReservationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([closure]);
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stillLive);

        var reconciled = await Service().ReconcileStaleClosuresAsync(TenantId, CancellationToken.None);

        reconciled.Should().Be(1);
        _closures.Verify(
            closures => closures.TryReleaseReservationAsync(
                TenantId, "sub-1", "M20260801T000000Z", closure.CloseOperationId!,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the cancellation that reserved this period never actually took effect, so ordinary " +
            "usage must not go on being refused for it");
        _closures.Verify(
            closures => closures.TryCommitClosingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_stale_reservation_under_an_operation_id_of_the_wrong_shape_is_left_untouched()
    {
        var boundary = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var closure = NewStaleClosure(boundary);
        closure.CloseOperationId = "not-a-cancellation-close-operation-id";

        _closures
            .Setup(closures => closures.ListStaleReservationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([closure]);

        var reconciled = await Service().ReconcileStaleClosuresAsync(TenantId, CancellationToken.None);

        reconciled.Should().Be(0,
            "an operation id that does not match a cancellation reservation's own deterministic " +
            "shape must never be guessed at");
        _closures.Verify(
            closures => closures.TryCommitClosingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _closures.Verify(
            closures => closures.TryReleaseReservationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _subscriptions.Verify(
            repository => repository.GetByIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "never even reaches for the subscription once the operation id itself is unrecognised");
    }

    /// <summary>
    /// Guards the P1 finding fixed alongside the overflow hardening: cancelling mid-trial must
    /// freeze the trial's own grant onto the outgoing window, before Status moves to Canceled and
    /// Trial is cleared — not the plain plan allowance a live resolve would find afterward.
    /// </summary>
    [Fact]
    public async Task An_immediate_cancellation_mid_trial_freezes_the_trial_grant_not_the_post_cancellation_allowance()
    {
        _subscription!.Status = SubscriptionStatus.Trialing;
        _subscription.Trial = new TrialTerms
        {
            StartsAtUtc = _time.GetUtcNow().UtcDateTime.AddDays(-4),
            EndsAtUtc = _time.GetUtcNow().UtcDateTime.AddDays(10),
            Grants = [new TrialMeterGrant { MeterKey = "screening", IncludedQuantity = 300 }]
        };
        _subscription.Plan.Meters =
        [
            new PlanMeter
            {
                MeterKey = "screening",
                IncludedQuantity = 500,
                ResetPolicy = MeterResetPolicy.Periodic,
                OverageAllowed = true
            }
        ];
        var usage = new Mock<ISubscriptionUsageRepository>();
        usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await ServiceWithUsage(usage.Object).CancelAsync(
            "sub-1", immediately: true, null, null, "corr-1", CancellationToken.None);

        _transition!.OutgoingUsagePeriod!.MeterAllowances.Should().NotBeNull(
            "an allowance/usage repository was supplied, so the snapshot must be captured");
        _transition.OutgoingUsagePeriod.MeterAllowances!["screening"].Should().Be(300,
            "the trial's own grant at the instant of cancellation, not the plan's plain 500 a " +
            "live resolve against the post-cancellation (no-trial) subscription would find");
    }

    [Fact]
    public async Task Reconciling_with_no_closure_repository_configured_does_nothing()
    {
        var service = new SubscriptionCancellationService(
            _subscriptions.Object,
            _links.Object,
            _contextResolver.Object,
            new SubscriptionOutboxEventFactory(),
            new SubscriptionResponseMapper(),
            _billingAccounts.Object,
            _cache.Object,
            NullLogger<SubscriptionCancellationService>.Instance,
            _time);

        var reconciled = await service.ReconcileStaleClosuresAsync(TenantId, CancellationToken.None);

        reconciled.Should().Be(0);
    }

    [Fact]
    public async Task A_crashed_immediate_escalation_releases_to_the_original_scheduled_boundary()
    {
        var escalationBoundary = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
        var closure = NewStaleClosure(escalationBoundary);
        var scheduled = NewSubscription();
        scheduled.CancelAtPeriodEnd = true;
        scheduled.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        _closures.Setup(repository => repository.ListStaleReservationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([closure]);
        _subscriptions.Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduled);

        await Service().ReconcileStaleClosuresAsync(TenantId, CancellationToken.None);

        _closures.Verify(repository => repository.TryReleaseReservationAsync(
            TenantId, "sub-1", closure.PeriodKey, closure.CloseOperationId!,
            It.IsAny<CancellationToken>()), Times.Once,
            "the failed escalation must not prevent the persisted September boundary from closing");
    }

    [Fact]
    public async Task A_stale_active_usage_claim_is_owned_then_released_by_recovery()
    {
        var claim = new UsagePeriodClaim
        {
            ItemId = "sub-1:M20260801T000000Z:usage-1",
            TenantId = TenantId,
            SubscriptionId = "sub-1",
            PeriodKey = "M20260801T000000Z",
            IdempotencyKey = "usage-1",
            State = UsagePeriodClaimState.Active,
            UpdatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        _closures.Setup(repository => repository.ListStaleReservationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _closures.Setup(repository => repository.ListStaleClaimsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([claim]);
        _closures.Setup(repository => repository.TryBeginStaleClaimRecoveryAsync(
                TenantId, claim.ItemId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var reconciled = await Service().ReconcileStaleClosuresAsync(TenantId, CancellationToken.None);

        reconciled.Should().Be(1);
        _closures.Verify(repository => repository.ReleaseClaimAsync(
            TenantId, "sub-1", claim.PeriodKey, "usage-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static UsagePeriodClosure NewStaleClosure(DateTime boundary) => new()
    {
        ItemId = "sub-1:M20260801T000000Z",
        TenantId = TenantId,
        SubscriptionId = "sub-1",
        PeriodKey = "M20260801T000000Z",
        State = UsagePeriodClosureState.CloseReserved,
        EffectiveEndUtc = boundary,
        CloseOperationId = $"cancellation-close:sub-1:{boundary.Ticks}",
        ReservationCreatedAtUtc = boundary.AddMinutes(-90)
    };

    /// <summary>
    /// The mock's TryTransitionAsync only records the transition; it does not, unlike the real
    /// repository, apply it. Tests that call CancelAsync a second time need the first call's
    /// effect folded into <see cref="_subscription"/> by hand.
    /// </summary>
    private void ApplyLastTransition()
    {
        _subscription!.CancelAtPeriodEnd = _transition!.CancelAtPeriodEnd ?? _subscription.CancelAtPeriodEnd;
        _subscription.CanCancelImmediately =
            _transition.CanCancelImmediately ?? _subscription.CanCancelImmediately;
        _subscription.CanceledAtUtc = _transition.CanceledAtUtc ?? _subscription.CanceledAtUtc;

        if (_transition.NewStatus != _transition.ExpectedStatus)
        {
            _subscription.Status = _transition.NewStatus;
            _subscription.EndedAtUtc = _transition.EndedAtUtc ?? _subscription.EndedAtUtc;
        }
    }

    private SubscriptionCancellationService Service() => new(
        _subscriptions.Object,
        _links.Object,
        _contextResolver.Object,
        new SubscriptionOutboxEventFactory(),
        new SubscriptionResponseMapper(),
        _billingAccounts.Object,
        _cache.Object,
        NullLogger<SubscriptionCancellationService>.Instance,
        _time,
        _scheduler.Object,
        _closures.Object);

    /// <summary>
    /// Only the allowance-snapshot tests need a real usage repository/resolver wired in — every
    /// other test above must keep exercising the legacy (no snapshot) path unchanged.
    /// </summary>
    private SubscriptionCancellationService ServiceWithUsage(ISubscriptionUsageRepository usage) => new(
        _subscriptions.Object,
        _links.Object,
        _contextResolver.Object,
        new SubscriptionOutboxEventFactory(),
        new SubscriptionResponseMapper(),
        _billingAccounts.Object,
        _cache.Object,
        NullLogger<SubscriptionCancellationService>.Instance,
        _time,
        _scheduler.Object,
        _closures.Object,
        options: null,
        usage: usage,
        allowances: new MeterAllowanceResolver(usage));

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        CurrentPeriodEndUtc = new DateTime(2026, 8, 31, 21, 59, 59, DateTimeKind.Utc),
        NextFeeBillingAtUtc = new DateTime(2026, 8, 31, 21, 59, 59, DateTimeKind.Utc),
        Plan = new PlanSnapshot { Code = "professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8900 }
    };
}
