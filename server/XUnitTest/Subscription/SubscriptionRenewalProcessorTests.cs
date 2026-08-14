using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>The periodic sweep that finds subscriptions due for a renewal or a dunning retry.</summary>
public sealed class SubscriptionRenewalProcessorTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionRenewalService> _renewals = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private IReadOnlyList<SubscriptionDetail> _due = [];

    public SubscriptionRenewalProcessorTests() =>
        _subscriptions
            .Setup(repository => repository.ListDueForRenewalAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _due);

    [Fact]
    public async Task Every_due_subscription_is_handed_to_the_renewal_service()
    {
        _due =
        [
            NewSubscription("sub-1"),
            NewSubscription("sub-2")
        ];

        var processed = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        processed.Should().Be(2);
        _renewals.Verify(
            renewals => renewals.RenewAsync(
                It.Is<SubscriptionDetail>(subscription => subscription.ItemId == "sub-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _renewals.Verify(
            renewals => renewals.RenewAsync(
                It.Is<SubscriptionDetail>(subscription => subscription.ItemId == "sub-2"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Nothing_due_processes_nothing()
    {
        _due = [];

        var processed = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        processed.Should().Be(0);
        _renewals.Verify(
            renewals => renewals.RenewAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task The_batch_size_setting_bounds_the_query()
    {
        _due = [NewSubscription("sub-1")];

        await Processor(batchSize: 5).ProcessDueAsync(TenantId, CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.ListDueForRenewalAsync(
                TenantId,
                It.IsAny<DateTime>(),
                5,
                It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_lost_compare_and_set_inside_the_renewal_service_is_not_treated_as_an_error()
    {
        _due = [NewSubscription("sub-1")];

        _renewals
            .Setup(renewals => renewals.RenewAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processed = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        processed.Should().Be(1, "the sweep counts what it attempted, not what changed");
    }

    private SubscriptionRenewalProcessor Processor(int batchSize = 50) => new(
        _subscriptions.Object,
        _renewals.Object,
        new OptionsStub(batchSize),
        NullLogger<SubscriptionRenewalProcessor>.Instance,
        _time);

    private static SubscriptionDetail NewSubscription(string id) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        Status = SubscriptionStatus.Active
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public OptionsStub(int batchSize) =>
            CurrentValue = new SubscriptionOptions { RenewalBatchSize = batchSize };

        public SubscriptionOptions CurrentValue { get; }

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
