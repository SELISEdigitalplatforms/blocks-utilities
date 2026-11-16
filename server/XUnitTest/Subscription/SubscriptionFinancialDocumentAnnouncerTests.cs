using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// The work key a document request is queued under. The queue keeps finished work and refuses a
/// second item with the same key, so the key decides whether a later request is queued at all.
/// </summary>
public sealed class SubscriptionFinancialDocumentAnnouncerTests
{
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly List<string> _keys = [];

    public SubscriptionFinancialDocumentAnnouncerTests() =>
        _scheduler
            .Setup(scheduler => scheduler.TryScheduleAsync(
                SubscriptionWorkType.FinancialDocumentIssue,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<SubscriptionWorkType, string, string, DateTime, string, string, string?, CancellationToken>(
                (_, _, key, _, _, _, _, _) => _keys.Add(key))
            .ReturnsAsync(true);

    [Fact]
    public async Task A_later_write_of_the_same_subscription_is_queued_as_its_own_occurrence()
    {
        var announcer = Announcer();

        await announcer.RequestPendingAsync(Subscription(version: 5), "corr-1", CancellationToken.None);
        await announcer.RequestPendingAsync(Subscription(version: 6), "corr-2", CancellationToken.None);

        _keys.Should().HaveCount(2).And.OnlyHaveUniqueItems(
            "a key shared with last month's finished item would be refused, leaving the invoice to the sweep");
        _keys.Should().AllSatisfy(key =>
            key.Should().StartWith(SubscriptionFinancialDocumentAnnouncer.SubscriptionWorkKeyPrefix,
                "the handler reads the prefix to know the aggregate is a subscription"));
    }

    [Fact]
    public async Task A_retried_request_for_the_same_write_lands_on_one_occurrence()
    {
        var announcer = Announcer();

        await announcer.RequestPendingAsync(Subscription(version: 5), "corr-1", CancellationToken.None);
        await announcer.RequestPendingAsync(Subscription(version: 5), "corr-1", CancellationToken.None);

        _keys.Distinct().Should().ContainSingle();
    }

    private SubscriptionFinancialDocumentAnnouncer Announcer() => new(
        _scheduler.Object,
        Mock.Of<ISubscriptionRepository>(),
        NullLogger<SubscriptionFinancialDocumentAnnouncer>.Instance);

    private static SubscriptionDetail Subscription(int version) => new()
    {
        ItemId = "sub-1",
        TenantId = "tenant-1",
        OrganizationId = "org-1",
        Version = version
    };
}
