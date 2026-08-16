using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The guarantees this module leans on that only a real database can demonstrate.
/// </summary>
/// <remarks>
/// Uniqueness, atomic increments and compare-and-set are all enforced by MongoDB, not by our
/// code. Mocking them would test the mock: every one of these would pass against an in-memory
/// stand-in while the real collection happily wrote a second live subscription or counted one
/// screening twice.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionRepositoryIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionRepository _subscriptions;
    private readonly SubscriptionUsageRepository _usage;
    private readonly SubscriptionPaymentLinkRepository _links;
    private readonly BillingAccountRepository _accounts;

    public SubscriptionRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);
        _usage = new SubscriptionUsageRepository(fixture.DbContextProvider);
        _links = new SubscriptionPaymentLinkRepository(fixture.DbContextProvider);
        _accounts = new BillingAccountRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task An_organization_cannot_hold_two_live_subscriptions()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _subscriptions.TryCreateAsync(
            NewSubscription(tenantId, "org-1", SubscriptionStatus.Active),
            CancellationToken.None)).Should().BeTrue();

        (await _subscriptions.TryCreateAsync(
                NewSubscription(tenantId, "org-1", SubscriptionStatus.Trialing),
                CancellationToken.None))
            .Should().BeFalse("the database refuses a second live subscription, so two " +
                              "concurrent signups cannot both succeed");
    }

    [Fact]
    public async Task An_ended_subscription_does_not_block_resubscribing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _subscriptions.TryCreateAsync(
            NewSubscription(tenantId, "org-2", SubscriptionStatus.Canceled),
            CancellationToken.None);

        (await _subscriptions.TryCreateAsync(
                NewSubscription(tenantId, "org-2", SubscriptionStatus.Active),
                CancellationToken.None))
            .Should().BeTrue("the uniqueness rule covers only statuses that grant something");
    }

    [Fact]
    public async Task Only_one_of_two_concurrent_transitions_wins()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId, "org-3", SubscriptionStatus.Incomplete);

        await _subscriptions.TryCreateAsync(
            subscription,
            CancellationToken.None);

        var transition = new SubscriptionTransition(
            SubscriptionStatus.Incomplete,
            SubscriptionStatus.Active)
        {
            ActivatedAtUtc = DateTime.UtcNow
        };

        var first = _subscriptions.TryTransitionAsync(
            tenantId,
            subscription.ItemId,
            transition,
            CancellationToken.None);

        var second = _subscriptions.TryTransitionAsync(
            tenantId,
            subscription.ItemId,
            transition,
            CancellationToken.None);

        var outcomes = await Task.WhenAll(first, second);

        outcomes.Count(succeeded => succeeded).Should().Be(1,
            "activation must apply once even if two sweeps pick up the same payment");

        var stored = await _subscriptions.GetByIdAsync(
            tenantId,
            subscription.ItemId,
            CancellationToken.None);

        stored!.Status.Should().Be(SubscriptionStatus.Active);
        stored.Version.Should().Be(2);
    }

    [Fact]
    public async Task The_same_usage_key_can_only_be_recorded_once()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _usage.TryAppendRecordAsync(
            NewUsageRecord(tenantId, "sub-1", "key-1"),
            CancellationToken.None)).Should().BeTrue();

        (await _usage.TryAppendRecordAsync(
                NewUsageRecord(tenantId, "sub-1", "key-1"),
                CancellationToken.None))
            .Should().BeFalse("a retried call must not become a second billable event");
    }

    [Fact]
    public async Task A_counter_returns_the_balance_including_the_callers_own_delta()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var seed = NewCounter(tenantId, "sub-2", "screening", "M20260801T000000Z");

        var first = await _usage.ApplyDeltaAsync(
            seed,
            1,
            CancellationToken.None);

        var second = await _usage.ApplyDeltaAsync(
            seed,
            1,
            CancellationToken.None);

        first.Balance.Should().Be(1);
        second.Balance.Should().Be(2, "the post-increment balance is what makes recording an " +
                                      "enforcement point rather than a check");
        second.AppliedRecordCount.Should().Be(2);
        second.LimitSnapshot.Should().Be(500, "the allowance is captured when the period opens");
    }

    [Fact]
    public async Task Concurrent_increments_all_land()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var seed = NewCounter(tenantId, "sub-3", "screening", "M20260801T000000Z");

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            _usage.ApplyDeltaAsync(seed, 1, CancellationToken.None)));

        var counter = await _usage.GetCounterAsync(
            tenantId,
            seed.ItemId,
            CancellationToken.None);

        counter!.Balance.Should().Be(20, "a read-modify-write would lose some of these");
    }

    [Fact]
    public async Task Adjacent_periods_are_separate_counters()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _usage.ApplyDeltaAsync(
            NewCounter(tenantId, "sub-4", "screening", "M20260801T000000Z"),
            5,
            CancellationToken.None);

        var next = await _usage.ApplyDeltaAsync(
            NewCounter(tenantId, "sub-4", "screening", "M20260901T000000Z"),
            1,
            CancellationToken.None);

        next.Balance.Should().Be(1, "a new period starts at zero without any rollover job");
    }

    [Fact]
    public async Task A_threshold_is_reported_by_exactly_one_caller()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var seed = NewCounter(tenantId, "sub-5", "screening", "M20260801T000000Z");

        await _usage.ApplyDeltaAsync(seed, 400, CancellationToken.None);

        var outcomes = await Task.WhenAll(
            _usage.TryMarkThresholdNotifiedAsync(
                tenantId, seed.ItemId, 80, CancellationToken.None),
            _usage.TryMarkThresholdNotifiedAsync(
                tenantId, seed.ItemId, 80, CancellationToken.None));

        outcomes.Count(won => won).Should().Be(1,
            "otherwise every screening past the threshold sends another email");
    }

    [Fact]
    public async Task A_payment_can_be_linked_to_only_one_subscription()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _links.TryCreateAsync(
            NewLink(tenantId, "sub-6", "pay-1"),
            CancellationToken.None)).Should().BeTrue();

        (await _links.TryCreateAsync(
                NewLink(tenantId, "sub-7", "pay-1"),
                CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_link_settles_once_however_many_sweeps_see_it()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var link = NewLink(tenantId, "sub-8", "pay-2");

        await _links.TryCreateAsync(link, CancellationToken.None);

        var outcomes = await Task.WhenAll(
            _links.TrySettleAsync(
                tenantId,
                link.ItemId,
                SubscriptionPaymentLinkState.Applied,
                CancellationToken.None),
            _links.TrySettleAsync(
                tenantId,
                link.ItemId,
                SubscriptionPaymentLinkState.Applied,
                CancellationToken.None));

        outcomes.Count(settled => settled).Should().Be(1);
    }

    [Fact]
    public async Task A_billing_account_is_created_once_per_organization_and_provider()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = await _accounts.GetOrCreateAsync(
            NewAccount(tenantId, "org-9"),
            CancellationToken.None);

        var second = await _accounts.GetOrCreateAsync(
            NewAccount(tenantId, "org-9"),
            CancellationToken.None);

        second.ItemId.Should().Be(first.ItemId);
    }

    [Fact]
    public async Task A_provider_customer_is_recorded_once_and_never_replaced()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var account = await _accounts.GetOrCreateAsync(
            NewAccount(tenantId, "org-10"),
            CancellationToken.None);

        (await _accounts.TrySetProviderCustomerAsync(
            tenantId, account.ItemId, "cus_first", "pm_1",
            CancellationToken.None)).Should().BeTrue();

        (await _accounts.TrySetProviderCustomerAsync(
                tenantId, account.ItemId, "cus_other", null,
                CancellationToken.None))
            .Should().BeFalse("adopting a second customer would strand every saved card on " +
                              "the first");

        var stored = await _accounts.GetAsync(
            tenantId,
            account.ItemId,
            CancellationToken.None);

        stored!.ProviderCustomerId.Should().Be("cus_first");
    }

    [Fact]
    public async Task An_event_is_appended_once_per_deduplication_key()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId, "org-11", SubscriptionStatus.Active);

        await _subscriptions.TryCreateAsync(
            subscription,
            CancellationToken.None);

        var outboxEvent = new SubscriptionOutboxEvent
        {
            EventType = "SubscriptionActivated",
            DeduplicationKey = $"{subscription.ItemId}:SubscriptionActivated",
            Payload = "{}",
            CorrelationId = "corr-1"
        };

        (await _subscriptions.TryAppendEventAsync(
            tenantId, subscription.ItemId, outboxEvent,
            CancellationToken.None)).Should().BeTrue();

        (await _subscriptions.TryAppendEventAsync(
                tenantId, subscription.ItemId, outboxEvent,
                CancellationToken.None))
            .Should().BeFalse();
    }

    private static SubscriptionDetail NewSubscription(
        string tenantId,
        string organizationId,
        SubscriptionStatus status)
    {
        var id = Guid.NewGuid().ToString();

        return new SubscriptionDetail
        {
            ItemId = id,
            TenantId = tenantId,
            OrganizationId = organizationId,
            BillingAccountId = "acct-1",
            Status = status,
            CurrencyCode = "CHF",
            OrderId = $"sub:{id}"
        };
    }

    private static SubscriptionUsageRecord NewUsageRecord(
        string tenantId,
        string subscriptionId,
        string idempotencyKey) => new()
    {
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = subscriptionId,
        MeterKey = "screening",
        PeriodKey = "M20260801T000000Z",
        Delta = 1,
        IdempotencyKey = idempotencyKey,
        OccurredAtUtc = DateTime.UtcNow
    };

    private static SubscriptionUsageCounter NewCounter(
        string tenantId,
        string subscriptionId,
        string meterKey,
        string periodKey) => new()
    {
        ItemId = SubscriptionUsageCounter.CreateId(subscriptionId, meterKey, periodKey),
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = subscriptionId,
        MeterKey = meterKey,
        PeriodKey = periodKey,
        LimitSnapshot = 500,
        PeriodStartUtc = DateTime.UtcNow.AddDays(-1),
        PeriodEndUtc = DateTime.UtcNow.AddDays(29),
        ExpiresAtUtc = DateTime.UtcNow.AddDays(400)
    };

    private static SubscriptionPaymentLink NewLink(
        string tenantId,
        string subscriptionId,
        string paymentDetailId) => new()
    {
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = subscriptionId,
        PaymentDetailId = paymentDetailId,
        OrderId = $"sub:{subscriptionId}"
    };

    private static BillingAccount NewAccount(string tenantId, string organizationId) => new()
    {
        TenantId = tenantId,
        OrganizationId = organizationId,
        ProviderName = "STRIPE"
    };
}
