using FluentAssertions;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The one edit that reaches existing subscribers: a meter that starts selling credits.
/// </summary>
/// <remarks>
/// Guards two customer-visible failures. A subscriber who signed up before the plan sold credits
/// must be able to buy them once it does, instead of being refused as "not for sale" beside a
/// newer subscriber who can. And a subscriber who already holds credit terms must keep the ones
/// they signed up with — a price raised in the catalogue afterwards must never reach them, which
/// is the snapshot rule every invoice depends on.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class CreditTermsAdoptionIntegrationTests
{
    private const string PlanId = "plan-1";
    private const string Meter = "screening";

    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionRepository _subscriptions;

    public CreditTermsAdoptionIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task A_subscriber_without_credit_terms_adopts_the_plans_new_ones()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await InsertAsync(tenantId, topUp: null, version: 3);

        var adopted = await _subscriptions.AdoptMeterTopUpAsync(
            tenantId, PlanId, Meter, Terms(unitAmountMinor: 125), CancellationToken.None);

        adopted.Should().Be(1);
        var stored = await StoredAsync(tenantId, subscription.ItemId);
        stored.Plan.Meters[0].TopUp.Should().NotBeNull(
            because: "the subscriber signed up before credits were sold and is now offered them");
        stored.Plan.Meters[0].TopUp!.UnitPrices[0].UnitAmountMinor.Should().Be(125);
        stored.Version.Should().Be(4,
            because: "a plan or quantity change that read the old snapshot must lose its " +
                     "compare-and-set rather than write that snapshot back over the terms");
    }

    [Fact]
    public async Task A_subscriber_who_already_holds_credit_terms_keeps_them()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await InsertAsync(tenantId, topUp: Terms(unitAmountMinor: 100), version: 3);

        var adopted = await _subscriptions.AdoptMeterTopUpAsync(
            tenantId, PlanId, Meter, Terms(unitAmountMinor: 125), CancellationToken.None);

        adopted.Should().Be(0);
        var stored = await StoredAsync(tenantId, subscription.ItemId);
        stored.Plan.Meters[0].TopUp!.UnitPrices[0].UnitAmountMinor.Should().Be(100,
            because: "a price raised in the catalogue reaches new subscribers only");
        stored.Version.Should().Be(3,
            because: "nothing changed, so nothing in flight should be told it conflicted");
    }

    [Fact]
    public async Task Only_the_named_meter_is_written()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await InsertAsync(tenantId, topUp: null, secondMeter: "reports");

        await _subscriptions.AdoptMeterTopUpAsync(
            tenantId, PlanId, "reports", Terms(unitAmountMinor: 125), CancellationToken.None);

        var stored = await StoredAsync(tenantId, subscription.ItemId);
        stored.Plan.Meters.Single(meter => meter.MeterKey == Meter).TopUp.Should().BeNull(
            because: "selling credits on one meter says nothing about the others");
        stored.Plan.Meters.Single(meter => meter.MeterKey == "reports").TopUp.Should().NotBeNull();
    }

    [Fact]
    public async Task A_subscriber_on_another_plan_or_no_longer_live_is_left_alone()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var otherPlan = await InsertAsync(tenantId, topUp: null, planId: "plan-2");
        var canceled = await InsertAsync(tenantId, topUp: null, status: SubscriptionStatus.Canceled);

        var adopted = await _subscriptions.AdoptMeterTopUpAsync(
            tenantId, PlanId, Meter, Terms(unitAmountMinor: 125), CancellationToken.None);

        adopted.Should().Be(0);
        (await StoredAsync(tenantId, otherPlan.ItemId)).Plan.Meters[0].TopUp.Should().BeNull(
            because: "the terms belong to one plan");
        (await StoredAsync(tenantId, canceled.ItemId)).Plan.Meters[0].TopUp.Should().BeNull(
            because: "a cancelled subscription is history and must stay as it was billed");
    }

    [Fact]
    public async Task Running_it_again_changes_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await InsertAsync(tenantId, topUp: null, version: 3);

        await _subscriptions.AdoptMeterTopUpAsync(
            tenantId, PlanId, Meter, Terms(unitAmountMinor: 125), CancellationToken.None);
        var second = await _subscriptions.AdoptMeterTopUpAsync(
            tenantId, PlanId, Meter, Terms(unitAmountMinor: 999), CancellationToken.None);

        second.Should().Be(0);
        var stored = await StoredAsync(tenantId, subscription.ItemId);
        stored.Plan.Meters[0].TopUp!.UnitPrices[0].UnitAmountMinor.Should().Be(125,
            because: "every plan edit calls this, so a second pass must not rewrite what the " +
                     "first one offered");
        stored.Version.Should().Be(4);
    }

    private static MeterTopUpTerms Terms(long unitAmountMinor) => new()
    {
        Enabled = true,
        UnitPrices = [new MeterTopUpPrice { CurrencyCode = "CHF", UnitAmountMinor = unitAmountMinor }],
        MinQuantity = 50,
        MaxQuantity = 500,
        ExpiryPolicy = MeterResetPolicy.CarryForward
    };

    private async Task<SubscriptionDetail> InsertAsync(
        string tenantId,
        MeterTopUpTerms? topUp,
        int version = 1,
        string planId = PlanId,
        SubscriptionStatus status = SubscriptionStatus.Active,
        string? secondMeter = null)
    {
        var subscription = new SubscriptionDetail
        {
            TenantId = tenantId,
            OrganizationId = "org-" + Guid.NewGuid().ToString("N"),
            Status = status,
            CurrencyCode = "CHF",
            Version = version,
            Plan = new PlanSnapshot
            {
                PlanId = planId,
                Code = "professional",
                Meters = [new PlanMeter { MeterKey = Meter, DisplayName = "Screening", TopUp = topUp }]
            }
        };

        if (secondMeter is not null)
        {
            subscription.Plan.Meters.Add(new PlanMeter { MeterKey = secondMeter, DisplayName = secondMeter });
        }

        await Raw(tenantId).InsertOneAsync(subscription, cancellationToken: CancellationToken.None);
        return subscription;
    }

    private async Task<SubscriptionDetail> StoredAsync(string tenantId, string itemId) =>
        await Raw(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.Eq(subscription => subscription.ItemId, itemId))
            .FirstAsync(CancellationToken.None);

    private IMongoCollection<SubscriptionDetail> Raw(string tenantId) =>
        _fixture.DbContextProvider
            .GetDatabase(tenantId)
            .GetCollection<SubscriptionDetail>("Subscriptions");
}
