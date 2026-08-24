using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;
using XUnitTest.Subscription;

namespace XUnitTest.Integration;

/// <summary>
/// The failure the scheduler's whole design claims to survive: the provider succeeded and the
/// worker died before recording it.
/// </summary>
/// <remarks>
/// Every part of that claim is asserted somewhere in the unit tests except the join between them,
/// and the join is the part that matters. A lease expiring, an item being reclaimed and a charge
/// not being raised twice are three separate facts; what a customer experiences is whether they
/// hold together.
/// <para>
/// Driven through the real <see cref="SubscriptionRenewalService"/> against a real database, because
/// the invariant belongs to production code: the charge is keyed on the period and the attempt
/// number, both read from persisted state. A test that derived the key itself would prove only that
/// the test can derive a key.
/// </para>
/// <para>
/// The crash is simulated by the state a crash leaves behind rather than by killing a process: the
/// charge is raised, and the write that would record it does not land. Here a settlement
/// reservation blocks the renewal transition — the same refusal the reservation work added — which
/// puts the subscription in exactly the position a killed pod would: money moved, renewal not
/// recorded, nothing holding the item.
/// </para>
/// </remarks>
public sealed class SubscriptionRenewalCrashRecoveryIntegrationTests
    : IClassFixture<MongoIntegrationFixture>
{
    private const string OrganizationId = "org-1";

    private readonly MongoIntegrationFixture _fixture;
    private readonly string _tenantId = MongoIntegrationFixture.NewTenantId();

    // Unique per test, because the item id is the document's _id and the class fixture shares one
    // database across the file: a second test reusing it inserts a duplicate and silently gets
    // nothing back.
    private readonly string _subscriptionId = Guid.NewGuid().ToString("N");
    private readonly string _accountId = Guid.NewGuid().ToString("N");
    private readonly RecordingGateway _gateway = new();

    /// <summary>Mid-period, so the renewal that is due closes the window that ended on the 1st.</summary>
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero));

    public SubscriptionRenewalCrashRecoveryIntegrationTests(MongoIntegrationFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task A_renewal_charged_but_not_recorded_is_finished_by_the_next_attempt_without_charging_twice()
    {
        var subscriptions = new SubscriptionRepository(_fixture.DbContextProvider);
        var accounts = new BillingAccountRepository(_fixture.DbContextProvider);

        await accounts.GetOrCreateAsync(NewAccount(), default);

        // Reserved, which is what makes the first attempt's transition fail after its charge — the
        // state a worker leaves behind when it dies between the provider and the write.
        var subscription = NewSubscription();
        subscription.SettlementReservation = new SettlementReservation
        {
            ReservationId = "reservation-1",
            Kind = SettlementReservationKind.QuantityIncrease,
            ChargeAmountMinor = 1_000,
            ReservedAtUtc = _time.GetUtcNow().UtcDateTime,
            CorrelationId = "corr-reservation"
        };

        (await subscriptions.TryCreateAsync(subscription, default))
            .Should().BeTrue("the seed must exist before anything is renewed");

        // ---- the attempt that is lost -------------------------------------------------------
        await Service(subscriptions, accounts).RenewAsync(subscription, default);

        _gateway.Charges.Should().ContainSingle("the provider was called, and it succeeded");

        var afterCrash = await Read(subscriptions);
        afterCrash.Status.Should().Be(SubscriptionStatus.Active);
        afterCrash.CurrentPeriodEndUtc.Should().Be(
            subscription.CurrentPeriodEndUtc,
            "the renewal was charged and never recorded, which is the whole point of this test");

        // ---- what the operator or the sweep clears before the retry --------------------------
        await subscriptions.TryReleaseSettlementAsync(
            _tenantId, afterCrash.ItemId, "reservation-1", default);

        // ---- the reclaim -------------------------------------------------------------------
        var reclaimed = await Read(subscriptions);

        await Service(subscriptions, accounts).RenewAsync(reclaimed, default);

        // The provider was asked twice and charged once. The second ask carried the same
        // idempotency key, because the key comes from the period and the attempt number — neither
        // of which the lost attempt moved.
        _gateway.Requests.Should().Be(2);
        _gateway.Charges.Should().ContainSingle(
            "a reclaimed renewal must find the charge it already raised, not raise another");

        var settled = await Read(subscriptions);
        settled.CurrentPeriodEndUtc.Should().BeAfter(
            subscription.CurrentPeriodEndUtc,
            "the second attempt recorded the renewal the first one paid for");
        settled.LastRenewalPaymentDetailId.Should().Be(_gateway.Charges.Single().Value);
        settled.DunningAttemptCount.Should().Be(0, "the renewal succeeded rather than declined");
    }

    [Fact]
    public async Task The_reclaimed_attempt_reuses_the_key_the_lost_attempt_raised()
    {
        // Stated separately because it is the mechanism, not the outcome: if this ever stops being
        // true, the test above starts passing for the wrong reason — two charges under two keys
        // would still leave one subscription renewed.
        var subscriptions = new SubscriptionRepository(_fixture.DbContextProvider);
        var accounts = new BillingAccountRepository(_fixture.DbContextProvider);

        await accounts.GetOrCreateAsync(NewAccount(), default);

        var subscription = NewSubscription();
        subscription.SettlementReservation = new SettlementReservation
        {
            ReservationId = "reservation-1",
            Kind = SettlementReservationKind.QuantityIncrease,
            ReservedAtUtc = _time.GetUtcNow().UtcDateTime,
            CorrelationId = "corr-reservation"
        };

        (await subscriptions.TryCreateAsync(subscription, default))
            .Should().BeTrue("the seed must exist before anything is renewed");

        await Service(subscriptions, accounts).RenewAsync(subscription, default);

        await subscriptions.TryReleaseSettlementAsync(
            _tenantId, subscription.ItemId, "reservation-1", default);

        await Service(subscriptions, accounts).RenewAsync(await Read(subscriptions), default);

        _gateway.Keys.Should().HaveCount(2);
        _gateway.Keys.Distinct(StringComparer.Ordinal).Should().ContainSingle(
            "both attempts belong to the same period and the same attempt number");
    }

    private SubscriptionRenewalService Service(
        ISubscriptionRepository subscriptions,
        IBillingAccountRepository accounts) =>
        new(
            subscriptions,
            accounts,
            _gateway,
            new SubscriptionOutboxEventFactory(),
            Mock.Of<IEntitlementSnapshotCache>(),
            new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
            NullLogger<SubscriptionRenewalService>.Instance,
            _time);

    private async Task<SubscriptionDetail> Read(ISubscriptionRepository subscriptions) =>
        (await subscriptions.GetAsync(_tenantId, OrganizationId, _subscriptionId, default))!;

    private BillingAccount NewAccount() => new()
    {
        ItemId = _accountId,
        TenantId = _tenantId,
        OrganizationId = OrganizationId,
        ProviderName = "STRIPE",
        DefaultPaymentMethodId = "pm-1",
        ProviderCustomerId = "cus_123"
    };

    private SubscriptionDetail NewSubscription() => new()
    {
        ItemId = _subscriptionId,
        TenantId = _tenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = _accountId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Version = 1,
        CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        NextFeeBillingAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8_900 },
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
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

    /// <summary>
    /// A provider that remembers what it charged, keyed the way a real one is.
    /// </summary>
    /// <remarks>
    /// The point of the whole exercise: asked twice under one idempotency key, it answers with the
    /// same payment rather than taking the money again. A real provider behaves this way, and this
    /// test exists to prove the client relies on it correctly rather than by accident.
    /// </remarks>
    private sealed class RecordingGateway : ISubscriptionBillingGateway
    {
        private readonly Dictionary<string, string> _charges = new(StringComparer.Ordinal);

        public List<string> Keys { get; } = [];

        public int Requests => Keys.Count;

        public IReadOnlyDictionary<string, string> Charges => _charges;

        public Task<SubscriptionOperationResult<string>> ChargeAsync(
            SubscriptionChargeRequest request,
            string idempotencyKey,
            string correlationId,
            CancellationToken cancellationToken)
        {
            Keys.Add(idempotencyKey);

            if (!_charges.TryGetValue(idempotencyKey, out var paymentId))
            {
                paymentId = $"pay-{_charges.Count + 1}";
                _charges[idempotencyKey] = paymentId;
            }

            return Task.FromResult(
                SubscriptionOperationResult<string>.Success(paymentId, correlationId));
        }
    }
}
