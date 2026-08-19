using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Messaging;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

public sealed class UsageThresholdEmailServiceTests
{
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<IMessageClient> _messages = new();

    [Fact]
    public async Task Threshold_event_queues_mail_for_the_subscription_billing_contact()
    {
        var subscription = Subscription();
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                "tenant-1", "subscription-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _accounts
            .Setup(repository => repository.GetAsync(
                "tenant-1", "account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                BillingEmail = " Owner@Example.com ",
                BillingName = "Ada Lovelace"
            });

        ConsumerMessage<SendMail>? queued = null;
        _messages
            .Setup(client => client.SendToConsumerAsync(
                It.IsAny<ConsumerMessage<SendMail>>()))
            .Callback<ConsumerMessage<SendMail>>(message => queued = message)
            .Returns(Task.CompletedTask);

        await Service().SendAsync(ThresholdEvent(), CancellationToken.None);

        queued.Should().NotBeNull();
        queued!.ConsumerName.Should().Be(SubscriptionConstants.MailQueue);
        queued.Payload.Purpose.Should().Be(
            SubscriptionConstants.UsageThresholdMailPurpose);
        queued.Payload.Language.Should().Be("en-US");
        queued.Payload.To.Should().Equal("owner@example.com");
        queued.Payload.BodyDataContext.Should().Contain(new Dictionary<string, string>
        {
            ["DisplayName"] = "Ada Lovelace",
            ["PlanName"] = "Claude Pro",
            ["PlanCode"] = "claude-pro",
            ["MeterKey"] = "tokens",
            ["ThresholdPercent"] = "80",
            ["Balance"] = "800",
            ["Limit"] = "1000"
        });
    }

    [Fact]
    public async Task Non_threshold_event_is_ignored()
    {
        var lifecycleEvent = ThresholdEvent();
        lifecycleEvent.EventType = SubscriptionConstants.SubscriptionActivated;

        await Service().SendAsync(lifecycleEvent, CancellationToken.None);

        _subscriptions.VerifyNoOtherCalls();
        _messages.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_billing_email_does_not_queue_an_invalid_mail()
    {
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                "tenant-1", "subscription-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Subscription());
        _accounts
            .Setup(repository => repository.GetAsync(
                "tenant-1", "account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount());

        await Service().SendAsync(ThresholdEvent(), CancellationToken.None);

        _messages.Verify(
            client => client.SendToConsumerAsync(
                It.IsAny<ConsumerMessage<SendMail>>()),
            Times.Never);
    }

    private UsageThresholdEmailService Service() => new(
        _subscriptions.Object,
        _accounts.Object,
        _messages.Object,
        NullLogger<UsageThresholdEmailService>.Instance);

    private static SubscriptionDetail Subscription() => new()
    {
        ItemId = "subscription-1",
        TenantId = "tenant-1",
        OrganizationId = "organization-1",
        BillingAccountId = "account-1",
        Plan = new PlanSnapshot
        {
            Code = "claude-pro",
            DisplayName = "Claude Pro"
        }
    };

    private static SubscriptionLifecycleEvent ThresholdEvent() => new()
    {
        EventId = "event-1",
        EventType = SubscriptionConstants.UsageThresholdReached,
        TenantId = "tenant-1",
        OrganizationId = "organization-1",
        SubscriptionId = "subscription-1",
        PlanCode = "claude-pro",
        MeterKey = "tokens",
        ThresholdPercent = 80,
        Balance = 800,
        Limit = 1000
    };
}
