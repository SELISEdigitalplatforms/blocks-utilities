using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;
using XUnitTest.Subscription.Credits;

namespace XUnitTest.Subscription.StandaloneCredits;

/// <summary>
/// Spending an organization's standalone credits at the usage gate (spec 005).
/// </summary>
/// <remarks>
/// Guards against credits an organization paid for being unreachable (no subscription, so nothing
/// recorded), being spent in the wrong order against a subscription's own allowance and grants,
/// being kept by a refused recording, or surviving the clock. And against a credits-only
/// organization being allowed to run on nothing once they are gone.
/// </remarks>
public sealed class OrganizationCreditConsumptionTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<IUsagePeriodClosureRepository> _closures = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IUsageThresholdEvaluator> _thresholds = new();
    private readonly Mock<IUsageProjectionPublisher> _projection = new();
    private readonly Mock<ISubscriptionUsageCurrentRepository> _current = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly Mock<ISubscriptionOutboxEventFactory> _events = new();
    private readonly Mock<IMessageClient> _messages = new();
    private readonly InMemoryCreditGrants _grants = new();
    private readonly InMemoryCreditOffers _offers = new();
    private readonly List<string> _published = [];
    private readonly List<SubscriptionUsageRecord> _records = [];

    private SubscriptionDetail? _subscription;
    private SubscriptionUsageCounter? _counter;

    public OrganizationCreditConsumptionTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetLiveAsync(TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);
        _subscriptions
            .Setup(repository => repository.TryAppendEventAsync(
                TenantId, SubscriptionId, It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _closures
            .Setup(repository => repository.TryAcquireClaimAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageClaimOutcome.Acquired);

        _usage
            .Setup(repository => repository.TryAppendRecordAsync(It.IsAny<SubscriptionUsageRecord>(), It.IsAny<CancellationToken>()))
            .Returns((SubscriptionUsageRecord record, CancellationToken _) =>
            {
                if (_records.Exists(existing => existing.SubscriptionId == record.SubscriptionId && existing.IdempotencyKey == record.IdempotencyKey))
                {
                    return Task.FromResult(false);
                }

                _records.Add(record);
                return Task.FromResult(true);
            });
        _usage
            .Setup(repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageCounter, decimal, CancellationToken>((seed, delta, _) =>
            {
                _counter ??= seed;
                _counter.Balance += delta;
                return Task.FromResult(_counter);
            });
        _usage
            .Setup(repository => repository.AddPurchasedConsumedAsync(
                TenantId, It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, decimal, CancellationToken>((_, _, delta, _) =>
            {
                _counter!.PurchasedConsumed += delta;
                return Task.FromResult<SubscriptionUsageCounter?>(_counter);
            });
        _usage
            .Setup(repository => repository.AddOrganizationCreditsConsumedAsync(
                TenantId, It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, decimal, CancellationToken>((_, _, delta, _) =>
            {
                _counter!.OrganizationCreditsConsumed += delta;
                return Task.FromResult<SubscriptionUsageCounter?>(_counter);
            });
        _usage
            .Setup(repository => repository.GetCounterAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _counter);

        _projection
            .Setup(publisher => publisher.PublishAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<PlanMeter>(), It.IsAny<BillingPeriod>(),
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<SubscriptionUsageCurrentSubLimit>?>()))
            .ReturnsAsync(UsageProjectionOutcome.Published);

        _events
            .Setup(factory => factory.CreateCreditEvent(
                It.IsAny<SubscriptionDetail>(), It.IsAny<string>(), It.IsAny<SubscriptionCreditGrant>(),
                It.IsAny<string>(), It.IsAny<int?>()))
            .Returns((SubscriptionDetail _, string type, SubscriptionCreditGrant _, string _, int? _) =>
            {
                _published.Add(type);
                return new SubscriptionOutboxEvent { EventType = type };
            });
        _messages
            .Setup(client => client.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<SubscriptionLifecycleEvent>>()))
            .Callback((ConsumerMessage<SubscriptionLifecycleEvent> message) => _published.Add(message.Payload.EventType))
            .Returns(Task.CompletedTask);

        _offers.All.Add(new CreditOffer { TenantId = TenantId, MeterKey = "ai", Enabled = true, MaxQuantity = 1_000 });
    }

    // ---- no subscription ----------------------------------------------------------------------

    [Fact]
    public async Task An_organization_with_no_subscription_spends_its_credits_and_is_billed_nothing()
    {
        _grants.All.Add(OrganizationGrant("g1", remaining: 10));

        var result = await Service().RecordAsync(Record(4), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Allowed.Should().BeTrue();
        result.Value.OrganizationCreditsConsumed.Should().Be(4);
        result.Value.OrganizationCreditsRemaining.Should().Be(6, "AC-8");
        _grants.All.Single().Remaining.Should().Be(6);
        _counter.Should().BeNull("no subscription, so no window counter and nothing an invoice could rate");
        _records.Should().ContainSingle(record => record.SubscriptionId.Length == 0 && record.Delta == 4);
    }

    [Fact]
    public async Task An_organization_with_no_subscription_and_no_credits_left_is_refused_whatever_enforce_says()
    {
        _grants.All.Add(OrganizationGrant("g1", remaining: 3));
        var request = Record(5);
        request.Enforce = false;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("subscription_credits_exhausted");
        result.ErrorMessage.Should().Be("No credits left for this meter. Buy more to continue.", "AC-9");
        _grants.All.Single().Remaining.Should().Be(3, "partly paid for is still refused, and the credits go back");
        _records.Should().HaveCount(2, "the use and its reversal both stay on the ledger");
    }

    [Fact]
    public async Task A_meter_no_offer_defines_is_unknown_to_an_organization_with_no_subscription()
    {
        var result = await Service().RecordAsync(Record(1, meterKey: "storage"), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should().Be("subscription_not_found", "AC-10");
        _records.Should().BeEmpty();
    }

    [Fact]
    public async Task A_repeated_recording_without_a_subscription_spends_nothing_twice()
    {
        _grants.All.Add(OrganizationGrant("g1", remaining: 10));
        var request = Record(4);
        var service = Service();

        await service.RecordAsync(request, "corr-1", CancellationToken.None);
        var retry = await service.RecordAsync(request, "corr-2", CancellationToken.None);

        retry.Value!.Replayed.Should().BeTrue();
        _grants.All.Single().Remaining.Should().Be(6, "at-least-once delivery must not spend credits twice");
    }

    [Fact]
    public async Task Emptying_the_last_organization_credits_announces_it_once()
    {
        _grants.All.Add(OrganizationGrant("g1", remaining: 4));

        await Service().RecordAsync(Record(4), "corr-1", CancellationToken.None);

        _published.Should().ContainSingle("AC-20: once, not once per grant")
            .Which.Should().Be(SubscriptionConstants.OrganizationCreditsExhausted);
    }

    [Fact]
    public async Task Current_usage_for_a_credits_only_organization_lists_its_credits_per_meter()
    {
        _grants.All.Add(OrganizationGrant("g1", remaining: 10));
        _grants.All.Add(OrganizationGrant("g2", remaining: 5, meterKey: "storage"));
        _grants.All.Add(OrganizationGrant("g3", remaining: 7, expiresAt: Now.UtcDateTime.AddDays(-1)));

        var result = await Service().ReadCurrentAsync(null, UsageReadMode.Projection, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Items.Should().HaveCount(2, "AC-23: one row per meter the organization holds live credits for");
        result.Value.Items.Single(item => item.MeterKey == "ai").OrganizationCreditsRemaining.Should().Be(10,
            "an expired grant is not something to spend");
    }

    // ---- beside a subscription ----------------------------------------------------------------

    [Fact]
    public async Task With_the_subscription_first_the_allowance_goes_before_the_organizations_credits()
    {
        Subscribed(allowanceUsed: 0);
        _grants.All.Add(OrganizationGrant("org", remaining: 10));

        var result = await Service().RecordAsync(Record(15), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Remaining.Should().Be(0, "AC-11: the plan's ten are spent");
        result.Value.Overage.Should().Be(0, "the five past the allowance were paid for with the organization's credits");
        result.Value.OrganizationCreditsConsumed.Should().Be(5);
        result.Value.OrganizationCreditsRemaining.Should().Be(5, "AC-11");
        _grants.All.Single().Remaining.Should().Be(5);
    }

    [Fact]
    public async Task With_the_organization_first_its_credits_go_before_the_allowance()
    {
        Subscribed(allowanceUsed: 0);
        _offers.All[0].SpendOrder = CreditSpendOrder.OrganizationCreditsFirst;
        _grants.All.Add(OrganizationGrant("org", remaining: 10));

        var result = await Service().RecordAsync(Record(15), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _grants.All.Single().Remaining.Should().Be(0, "AC-12: the organization's ten go first");
        result.Value!.OrganizationCreditsConsumed.Should().Be(10);
        result.Value.Remaining.Should().Be(5, "AC-12: five of the plan's ten are left");
        result.Value.Overage.Should().Be(0);
    }

    [Fact]
    public async Task With_the_subscription_first_its_own_grants_go_before_the_organizations_and_overage_comes_last()
    {
        Subscribed(allowanceUsed: 10);
        _grants.All.Add(SubscriptionGrant("sub", remaining: 3));
        _grants.All.Add(OrganizationGrant("org", remaining: 4));

        var result = await Service().RecordAsync(Record(10), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _grants.All.Single(grant => grant.ItemId == "sub").Remaining.Should().Be(0,
            "AC-13: the subscription's own grants are spent before the organization's");
        _grants.All.Single(grant => grant.ItemId == "org").Remaining.Should().Be(0);
        result.Value!.PurchasedConsumed.Should().Be(7, "three subscription credits and four organization credits");
        result.Value.OrganizationCreditsConsumed.Should().Be(4);
        result.Value.Overage.Should().Be(3, "AC-13: overage only once both are exhausted");
    }

    [Fact]
    public async Task A_refused_recording_gives_the_organizations_credits_back()
    {
        Subscribed(allowanceUsed: 10, overageAllowed: false);
        _grants.All.Add(OrganizationGrant("org", remaining: 3));

        var result = await Service().RecordAsync(Record(5), "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeFalse();
        _grants.All.Single().Remaining.Should().Be(3, "a refused recording must not keep the credits it drew");
        _counter!.PurchasedConsumed.Should().Be(0);
        _counter.OrganizationCreditsConsumed.Should().Be(0);
    }

    [Fact]
    public async Task A_disabled_offer_still_lets_live_credits_be_spent()
    {
        _offers.All[0].Enabled = false;
        _grants.All.Add(OrganizationGrant("g1", remaining: 10));

        var result = await Service().RecordAsync(Record(4), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue("AC-16: pausing sales refuses new purchases, never what was already bought");
        _grants.All.Single().Remaining.Should().Be(6);
    }

    // ---- lifecycle ------------------------------------------------------------------------------

    [Fact]
    public async Task Organization_credits_survive_a_plan_change_and_a_cancellation()
    {
        var grant = OrganizationGrant("g1", remaining: 10);
        var ended = new SubscriptionDetail { Status = SubscriptionStatus.Canceled, Plan = new PlanSnapshot() };

        CreditLedger.EffectiveStatus(grant, ended, Now.UtcDateTime).Should().Be(CreditGrantStatus.Active,
            "AC-14: nothing a subscription does forfeits what the organization bought outright");
        CreditLedger.EffectiveStatus(grant, null, Now.UtcDateTime).Should().Be(CreditGrantStatus.Active);

        _grants.All.Add(grant);
        var closed = await Ledger().MaintainAsync(TenantId, Now.UtcDateTime, TimeSpan.FromHours(24), 100, CancellationToken.None);
        closed.Should().Be(0);
    }

    [Fact]
    public async Task The_maintenance_sweep_closes_an_expired_organization_grant()
    {
        _grants.All.Add(OrganizationGrant("g1", remaining: 10, expiresAt: Now.UtcDateTime.AddMinutes(-1)));

        var closed = await Ledger().MaintainAsync(TenantId, Now.UtcDateTime, TimeSpan.FromHours(24), 100, CancellationToken.None);

        closed.Should().Be(1, "AC-20");
        _grants.All.Single().Status.Should().Be(CreditGrantStatus.Expired);
        _subscriptions.Verify(repository => repository.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "an organization grant has no subscription to look up");
    }

    // ---- helpers ------------------------------------------------------------------------------

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
        new RecordUsageRequestValidator(new SubscriptionOptionsMonitorStub(new SubscriptionOptions())),
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<UsageRecordingService>.Instance,
        new ControlledTimeProvider(Now),
        credits: Ledger());

    private CreditLedger Ledger() =>
        new(_grants, _usage.Object, _subscriptions.Object, _events.Object, _scheduler.Object, _offers, _messages.Object);

    /// <summary>A plan allowing ten, with a window already open at <paramref name="allowanceUsed"/>.</summary>
    private void Subscribed(decimal allowanceUsed, bool overageAllowed = true)
    {
        _subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            Status = SubscriptionStatus.Active,
            CurrencyCode = "USD",
            UsageSchedule = new BillingSchedule
            {
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                TimeZoneId = "UTC",
                AnchorDayOfMonth = 1
            },
            Plan = new PlanSnapshot
            {
                Code = "pro",
                Meters =
                [
                    new PlanMeter
                    {
                        MeterKey = "ai",
                        UnitLabel = "credit",
                        IncludedQuantity = 10,
                        OverageAllowed = overageAllowed,
                        TopUp = new MeterTopUpTerms { Enabled = true, MaxQuantity = 1_000 }
                    }
                ]
            }
        };
        _counter = new SubscriptionUsageCounter
        {
            ItemId = "counter-1",
            TenantId = TenantId,
            SubscriptionId = SubscriptionId,
            MeterKey = "ai",
            LimitSnapshot = 10,
            Balance = allowanceUsed
        };
    }

    private static RecordUsageRequest Record(decimal quantity, string meterKey = "ai") => new()
    {
        MeterKey = meterKey,
        Quantity = quantity,
        IdempotencyKey = $"use-{Guid.NewGuid():N}",
        Enforce = true
    };

    private static SubscriptionCreditGrant OrganizationGrant(
        string id,
        decimal remaining,
        string meterKey = "ai",
        DateTime? expiresAt = null) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        SubscriptionId = null,
        MeterKey = meterKey,
        Quantity = remaining,
        Remaining = remaining,
        ExpiryPolicy = MeterResetPolicy.Never,
        ExpiresAtUtc = expiresAt,
        GrantedAtUtc = Now.UtcDateTime.AddDays(-1)
    };

    private static SubscriptionCreditGrant SubscriptionGrant(string id, decimal remaining) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        SubscriptionId = SubscriptionId,
        MeterKey = "ai",
        Quantity = remaining,
        Remaining = remaining,
        ExpiryPolicy = MeterResetPolicy.Never,
        GrantedAtUtc = Now.UtcDateTime.AddDays(-1)
    };
}
