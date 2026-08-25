using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Noticing a threshold, once.
/// </summary>
public sealed class UsageThresholdEvaluatorTests
{
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly List<SubscriptionOutboxEvent> _events = [];
    private readonly HashSet<int> _alreadyNotified = [];

    public UsageThresholdEvaluatorTests()
    {
        _usage
            .Setup(repository => repository.TryMarkThresholdNotifiedAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, int, CancellationToken>((_, _, threshold, _) =>
                // Stands in for the conditional update: only the first caller modifies it.
                Task.FromResult(_alreadyNotified.Add(threshold)));

        _subscriptions
            .Setup(repository => repository.TryAppendEventAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionOutboxEvent, CancellationToken>(
                (_, _, outboxEvent, _) => _events.Add(outboxEvent))
            .ReturnsAsync(true);
    }

    [Theory]
    [InlineData(399, 0)]
    [InlineData(400, 1)]
    [InlineData(499, 1)]
    [InlineData(500, 2)]
    public async Task A_threshold_fires_exactly_when_it_is_reached(long balance, int expected)
    {
        var reported = await Evaluator().EvaluateAsync(
            NewSubscription(),
            NewCounter(balance),
            "corr-1",
            CancellationToken.None);

        reported.Should().Be(expected);
    }

    [Fact]
    public async Task A_threshold_is_reported_once_however_often_it_is_evaluated()
    {
        var subscription = NewSubscription();

        await Evaluator().EvaluateAsync(
            subscription, NewCounter(400), "corr-1", CancellationToken.None);

        var second = await Evaluator().EvaluateAsync(
            subscription, NewCounter(410), "corr-2", CancellationToken.None);

        second.Should().Be(0, "otherwise every unit past the threshold sends another alert");
        _events.Should().ContainSingle();
    }

    [Fact]
    public async Task Several_thresholds_crossed_at_once_are_reported_in_order()
    {
        await Evaluator().EvaluateAsync(
            NewSubscription(), NewCounter(500), "corr-1", CancellationToken.None);

        _events.Should().HaveCount(2);
        _events[0].Payload.Should().Contain("\"thresholdPercent\":80");
        _events[1].Payload.Should().Contain("\"thresholdPercent\":100");
    }

    [Fact]
    public async Task Each_threshold_gets_its_own_deduplication_key_per_period()
    {
        await Evaluator().EvaluateAsync(
            NewSubscription(), NewCounter(500), "corr-1", CancellationToken.None);

        _events.Select(outboxEvent => outboxEvent.DeduplicationKey)
            .Should().OnlyHaveUniqueItems()
            .And.AllSatisfy(key => key.Should().Contain("M20260801T000000Z",
                "crossing 80% in September is a different event from crossing it in August"));
    }

    [Fact]
    public async Task A_meter_with_no_thresholds_reports_nothing()
    {
        var subscription = NewSubscription();
        subscription.Plan.Meters[0].ThresholdPercents = [];

        var reported = await Evaluator().EvaluateAsync(
            subscription, NewCounter(500), "corr-1", CancellationToken.None);

        reported.Should().Be(0);
    }

    [Fact]
    public async Task A_counter_with_no_allowance_reports_nothing()
    {
        var counter = NewCounter(500);
        counter.LimitSnapshot = null;

        var reported = await Evaluator().EvaluateAsync(
            NewSubscription(), counter, "corr-1", CancellationToken.None);

        reported.Should().Be(0, "a percentage of nothing is not a crossing");
    }

    [Fact]
    public async Task Losing_the_race_to_report_raises_nothing()
    {
        _usage
            .Setup(repository => repository.TryMarkThresholdNotifiedAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var reported = await Evaluator().EvaluateAsync(
            NewSubscription(), NewCounter(500), "corr-1", CancellationToken.None);

        reported.Should().Be(0);
        _events.Should().BeEmpty("the winner has already told the customer");
    }

    private UsageThresholdEvaluator Evaluator() => new(
        _usage.Object,
        _subscriptions.Object,
        new SubscriptionOutboxEventFactory(),
        NullLogger<UsageThresholdEvaluator>.Instance);

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = "tenant-1",
        OrganizationId = "org-1",
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
                    ThresholdPercents = [80, 100]
                }
            ]
        }
    };

    private static SubscriptionUsageCounter NewCounter(long balance) => new()
    {
        ItemId = SubscriptionUsageCounter.CreateId(
            "sub-1", "screening", "M20260801T000000Z"),
        TenantId = "tenant-1",
        OrganizationId = "org-1",
        SubscriptionId = "sub-1",
        MeterKey = "screening",
        PeriodKey = "M20260801T000000Z",
        Balance = balance,
        LimitSnapshot = 500
    };
}
