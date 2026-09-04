using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Recording usage, and the guarantees a bill depends on.
/// </summary>
public sealed class UsageRecordingServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<IUsagePeriodClosureRepository> _closures = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IUsageThresholdEvaluator> _thresholds = new();
    private readonly Mock<IUsageProjectionPublisher> _projection = new();
    private readonly Mock<ISubscriptionUsageCurrentRepository> _current = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = NewSubscription();
    private decimal _balance;
    private readonly List<SubscriptionUsageRecord> _ledger = [];

    public UsageRecordingServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _closures
            .Setup(repository => repository.TryAcquireClaimAsync(
                TenantId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageClaimOutcome.Acquired);

        _usage
            .Setup(repository => repository.TryAppendRecordAsync(
                It.IsAny<SubscriptionUsageRecord>(), It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageRecord, CancellationToken>((record, _) =>
            {
                // Stands in for the unique index: the same key never lands twice.
                if (_ledger.Exists(existing => string.Equals(
                        existing.IdempotencyKey,
                        record.IdempotencyKey,
                        StringComparison.Ordinal)))
                {
                    return Task.FromResult(false);
                }

                _ledger.Add(record);

                return Task.FromResult(true);
            });

        _usage
            .Setup(repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageCounter, decimal, CancellationToken>((seed, delta, _) =>
            {
                _balance += delta;
                seed.Balance = _balance;

                return Task.FromResult(seed);
            });

        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SubscriptionUsageCounter { Balance = _balance });

        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => []);

        // The batch the current-usage read uses. Answers for whatever ids it is handed, so a meter
        // whose counter is absent is simply missing from the dictionary — which is how a window with
        // no usage reaches the response as a balance of zero.
        _usage
            .Setup(repository => repository.GetCountersAsync(
                TenantId,
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> ids, CancellationToken _) =>
                ids.ToDictionary(
                    id => id,
                    id => new SubscriptionUsageCounter { ItemId = id, Balance = _balance },
                    StringComparer.Ordinal));

        _projection
            .Setup(publisher => publisher.PublishAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<PlanMeter>(),
                It.IsAny<BillingPeriod>(),
                It.IsAny<SubscriptionUsageCounter>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageProjectionOutcome.Published);
    }

    [Fact]
    public async Task Recording_reports_the_balance_including_this_call()
    {
        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Used.Should().Be(1);
        result.Value.Remaining.Should().Be(499);
        result.Value.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task The_ledger_is_written_before_the_counter()
    {
        await Service().RecordAsync(NewRequest("usage-1"), "corr-1", CancellationToken.None);

        _ledger.Should().ContainSingle();
        _usage.Verify(
            repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_repeated_key_changes_nothing_and_says_so()
    {
        await Service().RecordAsync(NewRequest("usage-1"), "corr-1", CancellationToken.None);

        var replay = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-2", CancellationToken.None);

        replay.Value!.Replayed.Should().BeTrue();
        replay.Value.Used.Should().Be(1, "a retry must not become a second billable event");
        _ledger.Should().ContainSingle();
    }

    [Fact]
    public async Task A_usage_record_carries_its_correlation_id()
    {
        await Service().RecordAsync(NewRequest("usage-1"), "corr-1", CancellationToken.None);

        _ledger[0].CorrelationId.Should().Be("corr-1",
            "billing questions are answered months later, from this row alone");
        _ledger[0].PeriodKey.Should().NotBeEmpty(
            "stamping the period on write is what stops a record drifting into another month");
    }

    [Fact]
    public async Task Usage_past_the_allowance_is_recorded_as_overage_when_it_is_allowed()
    {
        _balance = 500;

        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeTrue();
        result.Value.Overage.Should().Be(1);
        result.Value.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task An_enforced_meter_past_its_allowance_refuses_and_rolls_back()
    {
        _subscription.Plan.Meters[0].OverageAllowed = false;
        _balance = 500;

        var request = NewRequest("usage-1");
        request.Enforce = true;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeFalse();
        result.Value.Used.Should().Be(500, "a refused call must leave the balance where it was");

        _ledger.Should().HaveCount(2);
        _ledger[1].EntryType.Should().Be(UsageEntryType.Reversal,
            "the ledger is append-only, so a refusal is recorded rather than erased");
        _ledger[1].Delta.Should().Be(-1);
        _ledger[1].CompensatesRecordId.Should().Be(_ledger[0].ItemId);
    }

    /// <summary>
    /// A future per-actor usage rollup sums <c>RecordedByUserId</c>-attributed ledger deltas, so a
    /// reversal must net out of the same actor who caused it — not land unattributed.
    /// </summary>
    [Fact]
    public async Task A_reversals_recorded_by_user_id_matches_the_record_it_reverses()
    {
        _subscription.Plan.Meters[0].OverageAllowed = false;
        _balance = 500;

        var request = NewRequest("usage-1");
        request.Enforce = true;

        await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        _ledger.Should().HaveCount(2);
        _ledger[0].RecordedByUserId.Should().Be("user-1");
        _ledger[1].EntryType.Should().Be(UsageEntryType.Reversal);
        _ledger[1].RecordedByUserId.Should().Be(_ledger[0].RecordedByUserId,
            "the reversal must be attributed to the same actor whose call it is undoing");
    }

    [Fact]
    public async Task A_trial_uses_its_own_grant_rather_than_the_plans_allowance()
    {
        _subscription.Status = SubscriptionStatus.Trialing;
        _subscription.Trial = new TrialTerms
        {
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddDays(14),
            Grants = [new TrialMeterGrant { MeterKey = "screening", IncludedQuantity = 25 }]
        };

        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.Value!.Included.Should().Be(25,
            "a trial that hands out the full monthly quota is an open invitation");
    }

    [Fact]
    public async Task A_meter_the_plan_does_not_define_is_not_found()
    {
        var request = NewRequest("usage-1");
        request.MeterKey = "not-a-meter";

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
    }

    [Fact]
    public async Task Usage_without_a_subscription_is_not_found()
    {
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_not_found");
    }

    /// <summary>
    /// A rejected claim is what actually enforces the boundary — not a re-read of the subscription
    /// document, which this test deliberately leaves showing Active/not-yet-cancelled to prove the
    /// claim is the thing doing the work.
    /// </summary>
    [Fact]
    public async Task Usage_rejected_by_a_claim_never_touches_the_ledger_or_counter()
    {
        _closures
            .Setup(repository => repository.TryAcquireClaimAsync(
                TenantId,
                _subscription.ItemId,
                It.IsAny<string>(),
                "usage-1",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageClaimOutcome.Rejected);

        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_not_found",
            "the same entitlement-denied answer as no subscription at all — not a new financial " +
            "state");
        _usage.Verify(
            repository => repository.TryAppendRecordAsync(
                It.IsAny<SubscriptionUsageRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a rejected claim must never reach the ledger");
        _usage.Verify(
            repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the counter this would have billed against must never be incremented");
        _closures.Verify(
            repository => repository.ReleaseClaimAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "there was never anything acquired to release");
    }

    [Fact]
    public async Task An_acquired_claim_is_released_after_a_successful_recording()
    {
        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _closures.Verify(
            repository => repository.ReleaseClaimAsync(
                TenantId, _subscription.ItemId, It.IsAny<string>(), "usage-1",
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a claim held for a write that succeeded must still be released, or the period can " +
            "never reach zero active writers");
    }

    [Fact]
    public async Task A_claim_reused_by_a_concurrent_duplicate_is_not_released_by_the_duplicate()
    {
        _closures
            .Setup(repository => repository.TryAcquireClaimAsync(
                TenantId,
                _subscription.ItemId,
                It.IsAny<string>(),
                "usage-1",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageClaimOutcome.AlreadyClaimed);

        await Service().RecordAsync(NewRequest("usage-1"), "corr-1", CancellationToken.None);

        _closures.Verify(
            repository => repository.ReleaseClaimAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "releasing here would end a claim the original, still in-flight caller has not " +
            "finished with — only the call that acquired a claim fresh may release it");
    }

    [Fact]
    public async Task A_missing_idempotency_key_is_refused()
    {
        var request = NewRequest("usage-1");
        request.IdempotencyKey = string.Empty;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unsupported_aggregation_is_refused_rather_than_summed()
    {
        _subscription.Plan.Meters[0].Aggregation = MeterAggregation.Max;

        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_meter_aggregation_unsupported");
        _ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task Late_reported_usage_lands_in_the_period_it_happened_in()
    {
        var request = NewRequest("usage-1");
        request.OccurredAtUtc = new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc);

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.Value!.PeriodStartUtc.Should().BeBefore(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_never_reset_meter_keeps_one_counter_across_month_boundaries()
    {
        _subscription.Plan.Meters[0].ResetPolicy = MeterResetPolicy.Never;

        var july = NewRequest("storage-july");
        july.OccurredAtUtc = new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc);
        var august = NewRequest("storage-august");
        august.OccurredAtUtc = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

        var first = await Service().RecordAsync(july, "corr-1", CancellationToken.None);
        var second = await Service().RecordAsync(august, "corr-2", CancellationToken.None);

        first.Value!.PeriodKey.Should().Be(MeterPeriodResolver.LifetimePeriodKey);
        second.Value!.PeriodKey.Should().Be(MeterPeriodResolver.LifetimePeriodKey);
        second.Value.Used.Should().Be(2, "renewal must not create a fresh storage allowance");
        _ledger.Select(record => record.PeriodKey).Should().OnlyContain(
            key => key == MeterPeriodResolver.LifetimePeriodKey);
    }

    [Fact]
    public async Task Deleting_storage_releases_lifetime_capacity()
    {
        _subscription.Plan.Meters[0].ResetPolicy = MeterResetPolicy.Never;
        _balance = 300;
        var request = NewRequest("storage-delete");
        request.Quantity = -100;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Used.Should().Be(200);
        result.Value.Remaining.Should().Be(300);
    }

    [Fact]
    public async Task A_periodic_consumption_meter_rejects_negative_usage()
    {
        var request = NewRequest("token-reduction");
        request.Quantity = -1;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_usage_reduction_not_allowed");
        _ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task A_capacity_reduction_cannot_take_the_balance_below_zero()
    {
        _subscription.Plan.Meters[0].ResetPolicy = MeterResetPolicy.Never;
        _balance = 50;
        var request = NewRequest("storage-delete");
        request.Quantity = -100;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeFalse();
        result.Value.Used.Should().Be(50);
        _ledger.Should().HaveCount(2);
        _ledger[1].EntryType.Should().Be(UsageEntryType.Reversal);
        _ledger[1].Delta.Should().Be(100);
    }

    [Fact]
    public async Task Current_usage_reads_periodic_and_lifetime_meters_from_different_counters()
    {
        _subscription.Plan.Meters.Add(new PlanMeter
        {
            MeterKey = "storage",
            UnitLabel = "byte",
            IncludedQuantity = 5_000,
            ResetPolicy = MeterResetPolicy.Never
        });

        await Service().GetCurrentUsageAsync(null, "corr-1", CancellationToken.None);

        // One batch for every meter, not one point read per meter.
        //
        // The two ids in it are also why the batch cannot be filtered by a single period key: a
        // never-reset capacity meter is addressed under LIFETIME while its periodic neighbour uses
        // the billing schedule's key, so one subscription's meters do not share a period. Filtering
        // by either one would have returned nothing for the other and reported it as unused.
        _usage.Verify(
            repository => repository.GetCountersAsync(
                TenantId,
                It.Is<IReadOnlyCollection<string>>(ids =>
                    ids.Count == 2 &&
                    ids.Contains(SubscriptionUsageCounter.CreateId(
                        "sub-1", "storage", MeterPeriodResolver.LifetimePeriodKey)) &&
                    ids.Any(id => id.StartsWith("sub-1:screening:M", StringComparison.Ordinal))),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _usage.Verify(
            repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the read must not fall back to a query per meter");
    }

    [Fact]
    public async Task A_requested_organization_is_forwarded_to_context_resolution()
    {
        var request = NewRequest("usage-1");
        request.OrganizationId = "org-9";

        await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    [Fact]
    public async Task A_requested_organization_on_get_current_usage_is_forwarded_to_context_resolution()
    {
        await Service().GetCurrentUsageAsync("org-9", "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    /// <summary>
    /// The projection may only answer when it holds every current window.
    /// </summary>
    /// <remarks>
    /// This subscription has two meters. With one document published, the earlier version of this
    /// returned that one meter and silently omitted the other — a caller drawing a usage screen from
    /// it would have shown half the meters, with nothing in the body to say so. Falling back is for
    /// the whole request, because a subset is not equivalent data.
    /// </remarks>
    [Fact]
    public async Task A_partly_published_projection_falls_back_to_the_counters_for_the_whole_request()
    {
        AddLifetimeMeter();

        _current
            .Setup(repository => repository.ListCurrentAsync(
                TenantId,
                OrganizationId,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Projected("screening")]);

        var read = await Service().ReadCurrentAsync(
            null, UsageReadMode.Projection, "corr-1", CancellationToken.None);

        read.IsSuccess.Should().BeTrue();
        read.Value!.Items.Should().HaveCount(2, "both meters, from the counters");
        read.Value.Diagnostics.ActualMode.Should().Be(UsageReadMode.Authoritative);
        read.Value.Diagnostics.Fallback.Should().Be(UsageReadFallback.ProjectionPartial);
    }

    /// <summary>An incomplete projection is a lost write, so it is repaired and not merely reported.</summary>
    [Fact]
    public async Task A_partly_published_projection_schedules_a_repair()
    {
        AddLifetimeMeter();

        _current
            .Setup(repository => repository.ListCurrentAsync(
                TenantId,
                OrganizationId,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Projected("screening")]);

        await Service().ReadCurrentAsync(
            null, UsageReadMode.Projection, "corr-1", CancellationToken.None);

        _scheduler.Verify(
            scheduler => scheduler.ScheduleUsageProjectionRefreshAsync(
                TenantId, OrganizationId, "sub-1", "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Nothing published is a different situation from some published, and is reported as such: it is
    /// a subscription the projection has never covered, which is a backfill matter rather than a lost
    /// write.
    /// </summary>
    [Fact]
    public async Task An_empty_projection_falls_back_and_says_it_was_empty_rather_than_partial()
    {
        AddLifetimeMeter();

        _current
            .Setup(repository => repository.ListCurrentAsync(
                TenantId,
                OrganizationId,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var read = await Service().ReadCurrentAsync(
            null, UsageReadMode.Projection, "corr-1", CancellationToken.None);

        read.Value!.Items.Should().HaveCount(2);
        read.Value.Diagnostics.Fallback.Should().Be(UsageReadFallback.ProjectionEmpty);
        _scheduler.Verify(
            scheduler => scheduler.ScheduleUsageProjectionRefreshAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "a subscription the projection has never covered is not a lost write");
    }

    [Fact]
    public async Task A_complete_projection_answers_the_read_without_touching_the_counters()
    {
        AddLifetimeMeter();

        _current
            .Setup(repository => repository.ListCurrentAsync(
                TenantId,
                OrganizationId,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Projected("screening"), Projected("storage")]);

        var read = await Service().ReadCurrentAsync(
            null, UsageReadMode.Projection, "corr-1", CancellationToken.None);

        read.Value!.Items.Should().HaveCount(2);
        read.Value.Diagnostics.ActualMode.Should().Be(UsageReadMode.Projection);
        read.Value.Diagnostics.Fallback.Should().Be(UsageReadFallback.None);

        _usage.Verify(
            repository => repository.GetCountersAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "answering from the projection is the entire point of the mode");
    }

    /// <summary>
    /// The default is unchanged, so no existing caller of this endpoint starts depending on a read
    /// model having been published.
    /// </summary>
    [Fact]
    public async Task The_default_read_is_authoritative()
    {
        var read = await Service().ReadCurrentAsync(
            null, UsageReadMode.Authoritative, "corr-1", CancellationToken.None);

        read.Value!.Diagnostics.ActualMode.Should().Be(UsageReadMode.Authoritative);
        read.Value.Diagnostics.Fallback.Should().Be(UsageReadFallback.None);

        _current.Verify(
            repository => repository.ListCurrentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Gives the subscription a second meter, so "the projection holds every window" is a claim about
    /// two of them rather than one. With a single meter, a partly-published projection cannot exist.
    /// </summary>
    private void AddLifetimeMeter() =>
        _subscription.Plan.Meters.Add(new PlanMeter
        {
            MeterKey = "storage",
            UnitLabel = "byte",
            IncludedQuantity = 5_000,
            ResetPolicy = MeterResetPolicy.Never
        });

    private static SubscriptionUsageCurrent Projected(string meterKey) => new()
    {
        ItemId = $"sub-1:{meterKey}:P",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        SubscriptionId = "sub-1",
        MeterKey = meterKey,
        UnitLabel = meterKey,
        PeriodKey = "P",
        PeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        Included = 500,
        Used = 3,
        Remaining = 497,
        UpdatedAtUtc = new DateTime(2026, 8, 14, 11, 59, 0, DateTimeKind.Utc)
    };

    private UsageRecordingService Service() => new(
        _subscriptions.Object,
        _usage.Object,
        _closures.Object,
        new MeterAllowanceResolver(_usage.Object),
        _contextResolver.Object,
        _thresholds.Object,
        _projection.Object,
        _current.Object,
        _scheduler.Object,
        new RecordUsageRequestValidator(new OptionsStub()),
        new OptionsStub(),
        NullLogger<UsageRecordingService>.Instance,
        _time);

    // ------------------------------------------------------------------ fractional quantities

    /// <summary>
    /// A meter that declares no scale refuses a fraction, which is how every meter behaved before
    /// fractional quantities existed. The guard the type widening would otherwise have removed:
    /// <c>0.5</c> used to be refused for free, because JSON could not bind it to a <c>long</c>.
    /// </summary>
    [Fact]
    public async Task A_whole_unit_meter_refuses_a_fraction()
    {
        var request = NewRequest("usage-1");
        request.Quantity = 0.5m;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_usage_quantity_scale_invalid");
        _ledger.Should().BeEmpty("nothing may reach the ledger that the meter cannot hold");
    }

    /// <summary>
    /// A bad average or a stray division producing 1.3333 on a whole-unit meter is refused rather
    /// than becoming a billable balance.
    /// </summary>
    [Fact]
    public async Task A_whole_unit_meter_refuses_a_repeating_fraction()
    {
        var request = NewRequest("usage-1");
        request.Quantity = 1.3333m;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_usage_quantity_scale_invalid");
        _ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task A_meter_that_declares_a_scale_records_a_fraction()
    {
        _subscription.Plan.Meters[0].QuantityScale = 3;

        var request = NewRequest("usage-1");
        request.Quantity = 512.5m;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _ledger.Should().ContainSingle().Which.Delta.Should().Be(512.5m);
    }

    /// <summary>
    /// The declared scale is a ceiling, not a licence: a quantity finer than the meter can hold is
    /// refused even on a meter that accepts fractions.
    /// </summary>
    [Fact]
    public async Task A_quantity_finer_than_the_declared_scale_is_refused()
    {
        _subscription.Plan.Meters[0].QuantityScale = 2;

        var request = NewRequest("usage-1");
        request.Quantity = 1.005m;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_usage_quantity_scale_invalid");
        _ledger.Should().BeEmpty();
    }

    /// <summary>
    /// Measured against the terms this subscription was sold, not the catalogue's current ones.
    /// </summary>
    /// <remarks>
    /// The snapshot is what its allowance and its rating are held to, so it has to be what its
    /// granularity is held to as well. Reading the live catalogue would let an edit to a plan
    /// change what an existing subscriber is allowed to report.
    /// </remarks>
    [Fact]
    public async Task The_scale_is_read_from_the_subscriptions_own_snapshot()
    {
        _subscription.Plan.Meters[0].QuantityScale = 1;

        var request = NewRequest("usage-1");
        request.Quantity = 0.5m;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _ledger.Should().ContainSingle().Which.Delta.Should().Be(0.5m);
    }

    /// <summary>
    /// A fractional reversal cancels the entry it compensates to the last place.
    /// </summary>
    /// <remarks>
    /// The reason quantities are exact decimals. Three additions of a third and one reversal of
    /// the whole must leave nothing behind; with binary floating point a residue would sit in the
    /// balance for the rest of the period and be billed as overage.
    /// </remarks>
    [Fact]
    public async Task A_fractional_release_returns_exactly_what_was_consumed()
    {
        AddLifetimeMeter();
        _subscription.Plan.Meters[1].QuantityScale = 6;

        var add = NewRequest("usage-1");
        add.MeterKey = "storage";
        add.Quantity = 0.333333m;

        await Service().RecordAsync(add, "corr-1", CancellationToken.None);
        await Service().RecordAsync(
            new RecordUsageRequest
            {
                MeterKey = "storage",
                Quantity = 0.333333m,
                IdempotencyKey = "usage-2"
            },
            "corr-2",
            CancellationToken.None);

        var release = new RecordUsageRequest
        {
            MeterKey = "storage",
            Quantity = -0.666666m,
            IdempotencyKey = "usage-3"
        };

        var result = await Service().RecordAsync(release, "corr-3", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Used.Should().Be(0m, "the release must cancel the consumption exactly");
    }

    /// <summary>
    /// Both read paths report the meter's granularity, so a caller need not read the plan's terms
    /// to know how to render the figures beside it.
    /// </summary>
    /// <remarks>
    /// Asserted on both because the two are contracted to be identical: a field on one and not the
    /// other would make the answer depend on whether the projection happened to be current.
    /// </remarks>
    [Fact]
    public async Task The_authoritative_read_reports_the_meters_granularity()
    {
        _subscription.Plan.Meters[0].QuantityScale = 3;

        var result = await Service().ReadCurrentAsync(
            null, UsageReadMode.Authoritative, "corr-1", CancellationToken.None);

        result.Value!.Items.Should().ContainSingle().Which.QuantityScale.Should().Be(3);
    }

    [Fact]
    public async Task The_projected_read_reports_the_meters_granularity()
    {
        _subscription.Plan.Meters[0].QuantityScale = 3;

        var projected = Projected("screening");
        projected.QuantityScale = 3;
        _current
            .Setup(repository => repository.ListCurrentAsync(
                TenantId,
                OrganizationId,
                "sub-1",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([projected]);

        var result = await Service().ReadCurrentAsync(
            null, UsageReadMode.Projection, "corr-1", CancellationToken.None);

        result.Value!.Items.Should().ContainSingle().Which.QuantityScale.Should().Be(3);
    }

    /// <summary>A meter that never opted in reports zero, which is whole units.</summary>
    [Fact]
    public async Task A_whole_unit_meter_reports_a_granularity_of_zero()
    {
        var result = await Service().ReadCurrentAsync(
            null, UsageReadMode.Authoritative, "corr-1", CancellationToken.None);

        result.Value!.Items.Should().ContainSingle().Which.QuantityScale.Should().Be(0);
    }

    /// <summary>Recording answers with it too, so one call tells a caller everything it needs.</summary>
    [Fact]
    public async Task Recording_reports_the_meters_granularity()
    {
        _subscription.Plan.Meters[0].QuantityScale = 2;

        var request = NewRequest("usage-1");
        request.Quantity = 1.25m;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.QuantityScale.Should().Be(2);
    }

    private static RecordUsageRequest NewRequest(string idempotencyKey) => new()
    {
        MeterKey = "screening",
        Quantity = 1,
        IdempotencyKey = idempotencyKey
    };

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot
        {
            Code = "professional",
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "screening",
                    UnitLabel = "screening",
                    IncludedQuantity = 500,
                    OverageAllowed = true,
                    ThresholdPercents = [80, 100]
                }
            ]
        },
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        }
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
