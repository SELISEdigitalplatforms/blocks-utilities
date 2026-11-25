using FluentAssertions;
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

namespace XUnitTest.Subscription;

/// <summary>
/// Pushing a meter's overage rates to a plan's subscribers from the work queue: the rates come from
/// the catalogue when the work runs, and the meter key survives the trip through the work key.
/// </summary>
public sealed class PlanMeterRatesPropagationWorkHandlerTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();

    [Theory]
    [InlineData("api-calls")]
    [InlineData("calls/minute")]
    public void A_meter_key_comes_back_whole_from_its_work_key_even_with_the_separator_in_it(string meterKey)
    {
        var key = PlanMeterRatesPropagationWorkHandler.KeyFor("plan-1", "0HN7:00000001", meterKey);

        PlanMeterRatesPropagationWorkHandler.MeterKeyOf(key).Should().Be(meterKey);
    }

    [Fact]
    public async Task The_rates_pushed_are_the_catalogues_when_the_work_runs()
    {
        var plan = new Plan
        {
            ItemId = "plan-1",
            TenantId = TenantId,
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "api-calls",
                    RateTables = [new MeterRateTable { CurrencyCode = "CHF", Tiers = [new MeterTier { UnitAmountMinor = 100 }] }]
                }
            ]
        };
        _catalogue.Setup(repository => repository.GetPlanAsync(TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        _subscriptions.Setup(repository => repository.ListRenewableOnPlanAsync(
                TenantId, "plan-1", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var outcome = await Handler().ExecuteAsync(Work("api-calls"), CancellationToken.None);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _subscriptions.Verify(repository => repository.ListRenewableOnPlanAsync(
            TenantId, "plan-1", null, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_plan_whose_meter_is_gone_is_given_up_on_rather_than_retried()
    {
        _catalogue.Setup(repository => repository.GetPlanAsync(TenantId, "plan-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Plan { ItemId = "plan-1", TenantId = TenantId });

        var outcome = await Handler().ExecuteAsync(Work("api-calls"), CancellationToken.None);

        outcome.Result.Should().Be(SubscriptionWorkResult.Permanent);
        outcome.ErrorCode.Should().Be("plan_meter_not_found");
    }

    private static SubscriptionBackgroundWork Work(string meterKey) => new()
    {
        TenantId = TenantId,
        WorkType = SubscriptionWorkType.PlanMeterRatesPropagation,
        AggregateId = "plan-1",
        WorkKey = PlanMeterRatesPropagationWorkHandler.KeyFor("plan-1", "corr-1", meterKey),
        CorrelationId = "corr-1"
    };

    private PlanMeterRatesPropagationWorkHandler Handler() => new(
        _catalogue.Object,
        new ExistingSubscriberPriceTerms(
            _subscriptions.Object,
            new SubscriptionOutboxEventFactory(),
            Mock.Of<IOptionsMonitor<SubscriptionOptions>>(options => options.CurrentValue == new SubscriptionOptions()),
            TimeProvider.System,
            NullLogger<ExistingSubscriberPriceTerms>.Instance));
}
