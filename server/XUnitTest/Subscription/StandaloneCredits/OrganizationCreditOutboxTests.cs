using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;
using XUnitTest.Subscription.Credits;

namespace XUnitTest.Subscription.StandaloneCredits;

/// <summary>
/// The organization's outbox (spec 005): its credit events ride on the grant they are about.
/// </summary>
/// <remarks>
/// Guards against an organization's purchase, grant or exhaustion going unannounced when the
/// broker is down at the moment it happens, against one being announced twice when the write that
/// caused it is retried, and against the confirmation mail's figures and recipient outliving the
/// mail on the grant document.
/// </remarks>
public sealed class OrganizationCreditOutboxTests
{
    private const string TenantId = "tenant-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryCreditGrants _grants = new();
    private readonly Mock<IMessageClient> _messages = new();
    private readonly ControlledTimeProvider _time = new(Now);
    private readonly List<SubscriptionLifecycleEvent> _sent = [];
    private readonly SubscriptionOptions _options = new();
    private int _outages;

    public OrganizationCreditOutboxTests()
    {
        _messages
            .Setup(client => client.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<SubscriptionLifecycleEvent>>()))
            .Returns((ConsumerMessage<SubscriptionLifecycleEvent> message) =>
            {
                if (_outages > 0)
                {
                    _outages--;
                    throw new InvalidOperationException("broker unreachable");
                }

                _sent.Add(message.Payload);
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task A_grant_written_with_its_event_announces_it_once_and_keeps_only_the_key_afterwards()
    {
        var purchase = new SubscriptionCreditPurchase
        {
            ItemId = "org-1:k1", TenantId = TenantId, OrganizationId = "org-1", MeterDisplayName = "AI",
            TotalMinor = 865, CurrencyCode = "USD", BillingAccountId = "account-9", PackCode = "starter"
        };
        var grant = Grant("g1");
        grant.OutboxEvents.Add(CreditLedger.OrganizationEvent(
            grant, SubscriptionConstants.OrganizationCreditsPurchased, "corr-1", purchase, Now.UtcDateTime));
        await _grants.TryInsertAsync(grant, CancellationToken.None);

        var ledger = Ledger();
        await ledger.PublishOrganizationAsync(grant, SubscriptionConstants.OrganizationCreditsPurchased, "corr-1", CancellationToken.None, purchase);
        await ledger.PublishOrganizationAsync(grant, SubscriptionConstants.OrganizationCreditsPurchased, "corr-1", CancellationToken.None, purchase);

        var sent = _sent.Should().ContainSingle("the event was written once, so it is sent once").Subject;
        sent.EventType.Should().Be(SubscriptionConstants.OrganizationCreditsPurchased);
        sent.CreditPurchaseId.Should().Be("org-1:k1");
        sent.CreditTotalMinor.Should().Be(865);
        sent.CurrencyCode.Should().Be("USD");
        sent.BillingAccountId.Should().Be("account-9", "the confirmation mail is addressed through the account charged");
        sent.MeterDisplayName.Should().Be("AI");
        sent.PackCode.Should().Be("starter");
        sent.CreditQuantity.Should().Be(100);
        sent.CreditExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30));

        var stored = _grants.All[0].OutboxEvents.Should().ContainSingle().Subject;
        stored.Status.Should().Be(SubscriptionOutboxStatus.Published);
        stored.DeduplicationKey.Should().Be("g1:OrganizationCreditsPurchased");
        stored.Payload.Should().BeEmpty(
            "a purchase event becomes a mail and names the billing account; nothing reads it once sent");
    }

    [Fact]
    public async Task A_broker_outage_leaves_the_event_on_the_grant_for_the_sweep_rather_than_losing_it()
    {
        var grant = Grant("g1");
        grant.OutboxEvents.Add(CreditLedger.OrganizationEvent(grant, SubscriptionConstants.OrganizationCreditsGranted, "corr-1"));
        await _grants.TryInsertAsync(grant, CancellationToken.None);
        _outages = 1;

        var announce = () => Ledger().PublishOrganizationAsync(
            grant, SubscriptionConstants.OrganizationCreditsGranted, "corr-1", CancellationToken.None);

        await announce.Should().NotThrowAsync("the grant is already durable; the caller's own operation must not fail");
        _sent.Should().BeEmpty();
        var pending = _grants.All[0].OutboxEvents.Should().ContainSingle().Subject;
        pending.Status.Should().Be(SubscriptionOutboxStatus.RetryScheduled);
        pending.AttemptCount.Should().Be(1);
        pending.NextAttemptAtUtc.Should().Be(Now.UtcDateTime.AddSeconds(2), "the first retry backs off two seconds");

        _time.Advance(TimeSpan.FromSeconds(1));
        (await Processor().PublishDueAsync(TenantId, CancellationToken.None)).Should().Be(0, "not yet due");

        _time.Advance(TimeSpan.FromSeconds(2));
        (await Processor().PublishDueAsync(TenantId, CancellationToken.None)).Should().Be(1);

        _sent.Should().ContainSingle().Which.EventType.Should().Be(SubscriptionConstants.OrganizationCreditsGranted);
        _grants.All[0].OutboxEvents[0].Status.Should().Be(SubscriptionOutboxStatus.Published);
        _grants.All[0].OutboxEvents[0].Payload.Should().NotBeEmpty(
            "a grant event is not a mail and keeps its payload, as subscription events do");
    }

    [Fact]
    public async Task Exhaustion_is_announced_once_however_often_the_draw_that_emptied_the_grant_is_retried()
    {
        var grant = Grant("g1", remaining: 0, status: CreditGrantStatus.Exhausted);
        await _grants.TryInsertAsync(grant, CancellationToken.None);
        var ledger = Ledger();

        await ledger.PublishOrganizationAsync(grant, SubscriptionConstants.OrganizationCreditsExhausted, "corr-1", CancellationToken.None);
        await ledger.PublishOrganizationAsync(grant, SubscriptionConstants.OrganizationCreditsExhausted, "corr-2", CancellationToken.None);

        _grants.All[0].OutboxEvents.Should().ContainSingle("the key is the grant and the event type, and a grant empties once");
        _sent.Should().ContainSingle().Which.CreditRemaining.Should().Be(0);
    }

    [Fact]
    public async Task An_event_that_fails_its_whole_attempt_budget_is_abandoned_and_the_sweep_lets_it_go()
    {
        _options.OutboxMaxAttempts = 1;
        var grant = Grant("g1");
        grant.OutboxEvents.Add(CreditLedger.OrganizationEvent(grant, SubscriptionConstants.OrganizationCreditsGranted, "corr-1"));
        await _grants.TryInsertAsync(grant, CancellationToken.None);
        _outages = 5;

        await Ledger().PublishOrganizationAsync(grant, SubscriptionConstants.OrganizationCreditsGranted, "corr-1", CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(10));
        var swept = await Processor().PublishDueAsync(TenantId, CancellationToken.None);

        _grants.All[0].OutboxEvents[0].Status.Should().Be(SubscriptionOutboxStatus.Abandoned);
        _grants.All[0].OutboxEvents[0].LastError.Should().Be("broker unreachable", "an operator needs to see why");
        swept.Should().Be(0, "an abandoned event is not retried forever");
    }

    [Fact]
    public async Task The_stored_payload_round_trips_through_the_processor_exactly_as_the_topic_expects()
    {
        var grant = Grant("g1");
        var outboxEvent = CreditLedger.OrganizationEvent(grant, SubscriptionConstants.OrganizationCreditsGranted, "corr-7", nowUtc: Now.UtcDateTime);

        var payload = JsonSerializer.Deserialize<SubscriptionLifecycleEvent>(
            outboxEvent.Payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        payload!.EventId.Should().Be(outboxEvent.EventId);
        payload.CorrelationId.Should().Be("corr-7");
        payload.OrganizationId.Should().Be("org-1");
        payload.SubscriptionId.Should().BeEmpty("an organization event belongs to no subscription");
        payload.OccurredAtUtc.Should().Be(Now.UtcDateTime);
        outboxEvent.CorrelationId.Should().Be("corr-7", "the trace must survive the queue hop");
    }

    private CreditLedger Ledger() => new(
        _grants,
        Mock.Of<ISubscriptionUsageRepository>(),
        Mock.Of<ISubscriptionRepository>(),
        Mock.Of<ISubscriptionOutboxEventFactory>(),
        Mock.Of<ISubscriptionWorkScheduler>(),
        new InMemoryCreditOffers(),
        Processor());

    private OrganizationCreditOutboxProcessor Processor() => new(
        _grants,
        _messages.Object,
        new SubscriptionOptionsMonitorStub(_options),
        NullLogger<OrganizationCreditOutboxProcessor>.Instance,
        _time);

    private static SubscriptionCreditGrant Grant(
        string id,
        decimal remaining = 100,
        CreditGrantStatus status = CreditGrantStatus.Active) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        OrganizationId = "org-1",
        SubscriptionId = null,
        MeterKey = "ai",
        Source = CreditGrantSource.Purchased,
        Quantity = 100,
        Remaining = remaining,
        Status = status,
        ExpiresAtUtc = Now.UtcDateTime.AddDays(30),
        GrantedAtUtc = Now.UtcDateTime
    };
}
