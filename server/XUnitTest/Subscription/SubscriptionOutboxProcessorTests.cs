using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// What the outbox keeps of an event once it has been published (spec 001, AC-10).
/// </summary>
/// <remarks>
/// Guards against people's names and a canceller's own words living on the subscription document
/// for as long as the deduplication key must — years — after the mail that needed them has gone.
/// </remarks>
public sealed class SubscriptionOutboxProcessorTests
{
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IMessageClient> _messages = new();
    private readonly Dictionary<string, bool> _cleared = [];

    public SubscriptionOutboxProcessorTests()
    {
        _subscriptions
            .Setup(repository => repository.TryClaimEventAsync(
                "tenant-1", "sub-1", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string eventId, string _, DateTime _, CancellationToken _) =>
                Events.Single(outboxEvent => outboxEvent.EventId == eventId));
        _subscriptions
            .Setup(repository => repository.MarkEventPublishedAsync(
                "tenant-1", "sub-1", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, DateTime, bool, CancellationToken>(
                (_, _, eventId, _, _, clearPayload, _) => _cleared[eventId] = clearPayload)
            .Returns(Task.CompletedTask);
        _messages
            .Setup(client => client.SendToMassConsumerAsync(
                It.IsAny<ConsumerMessage<SubscriptionLifecycleEvent>>()))
            .Returns(Task.CompletedTask);
    }

    private static readonly SubscriptionOutboxEvent[] Events =
    [
        Event("canceled", SubscriptionConstants.SubscriptionCanceled),
        Event("renewed", SubscriptionConstants.SubscriptionRenewed)
    ];

    [Fact]
    public async Task Only_a_notification_events_payload_is_cleared_once_published()
    {
        var subscription = new SubscriptionDetail
        {
            ItemId = "sub-1",
            TenantId = "tenant-1",
            OutboxEvents = [.. Events]
        };

        var published = await Processor().PublishDueForSubscriptionAsync(
            subscription, CancellationToken.None);

        published.Should().Be(2);
        _cleared["canceled"].Should().BeTrue(
            "its payload names who cancelled and why, and nothing reads it after publishing");
        _cleared["renewed"].Should().BeFalse(
            "events that are not notifications keep their payload exactly as before");
    }

    private SubscriptionOutboxProcessor Processor()
    {
        var options = new Mock<IOptionsMonitor<SubscriptionOptions>>();
        options.Setup(monitor => monitor.CurrentValue).Returns(new SubscriptionOptions());

        return new SubscriptionOutboxProcessor(
            _subscriptions.Object,
            _messages.Object,
            options.Object,
            NullLogger<SubscriptionOutboxProcessor>.Instance);
    }

    private static SubscriptionOutboxEvent Event(string eventId, string eventType) => new()
    {
        EventId = eventId,
        EventType = eventType,
        DeduplicationKey = $"sub-1:{eventType}",
        Payload = $$"""{"eventId":"{{eventId}}","eventType":"{{eventType}}"}"""
    };
}
