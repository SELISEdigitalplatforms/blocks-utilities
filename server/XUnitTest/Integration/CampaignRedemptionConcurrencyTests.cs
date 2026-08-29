using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The guarantee this whole feature's "one use per organization" claim rests on, proven against a
/// real MongoDB rather than argued about in a code comment.
/// </summary>
/// <remarks>
/// <see cref="CampaignRedemptionRepository"/> is deliberately not tested with a mock anywhere in
/// this suite. A mock answers whatever it is told to, and the entire risk in this class is
/// exactly the part a mock cannot represent: what two callers racing for the same document
/// actually see from the database's own atomicity, not from an assumption about how a method
/// happens to be sequenced.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class CampaignRedemptionConcurrencyTests
{
    private readonly MongoIntegrationFixture _fixture;

    public CampaignRedemptionConcurrencyTests(MongoIntegrationFixture fixture) => _fixture = fixture;

    private ICampaignRedemptionRepository Repository() => new CampaignRedemptionRepository(_fixture.DbContextProvider);

    [Fact]
    public async Task Two_different_subscriptions_racing_for_a_one_use_campaign_converge_on_exactly_one_winner()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        var discountId = Guid.NewGuid().ToString();
        var organizationId = "org-race";

        // Twenty concurrent attempts, all for the same one-use slot, from twenty different
        // subscriptions. Genuine concurrency, not a sequence of awaits that merely looks
        // concurrent: every task starts before any of them is awaited.
        var attempts = Enumerable.Range(0, 20)
            .Select(_ => Guid.NewGuid().ToString())
            .Select(subscriptionId => repository.TryReserveAsync(
                Reservation(tenantId, organizationId, discountId, subscriptionId, oneUse: true),
                CancellationToken.None))
            .ToArray();

        var outcomes = await Task.WhenAll(attempts);

        outcomes.Count(outcome => outcome == CampaignReservationOutcome.Reserved).Should().Be(1);
        outcomes.Count(outcome => outcome == CampaignReservationOutcome.HeldByAnotherSubscription)
            .Should().Be(19);
    }

    [Fact]
    public async Task Retrying_the_same_subscription_against_the_same_campaign_always_succeeds()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        var discountId = Guid.NewGuid().ToString();
        var organizationId = "org-retry";
        var subscriptionId = Guid.NewGuid().ToString();

        var first = await repository.TryReserveAsync(
            Reservation(tenantId, organizationId, discountId, subscriptionId, oneUse: true),
            CancellationToken.None);

        // Fifty retries of the exact same subscription, concurrently -- the shape a network-level
        // retry storm against one slow request actually takes, not fifty neat sequential calls.
        // High count deliberately: this is the exact window a naive "check then insert" gets
        // wrong -- two concurrent attempts for the very same subscription can both pass the
        // existence check before either one's insert lands, and the loser must recognise the
        // winner as itself rather than as a stranger. A count of ten did not reproduce that
        // window on this machine; fifty reliably does.
        var retries = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            repository.TryReserveAsync(
                Reservation(tenantId, organizationId, discountId, subscriptionId, oneUse: true),
                CancellationToken.None)));

        first.Should().Be(CampaignReservationOutcome.Reserved);
        retries.Should().OnlyContain(outcome =>
            outcome == CampaignReservationOutcome.Reserved ||
            outcome == CampaignReservationOutcome.AlreadyReservedBySameSubscription);

        var stored = await repository.FindAsync(tenantId, discountId, subscriptionId, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.State.Should().Be(CampaignRedemptionState.Reserved);
    }

    [Fact]
    public async Task A_non_one_use_campaign_lets_every_subscription_reserve_its_own_row()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        var discountId = Guid.NewGuid().ToString();
        var organizationId = "org-shared";

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Guid.NewGuid().ToString())
            .Select(subscriptionId => repository.TryReserveAsync(
                Reservation(tenantId, organizationId, discountId, subscriptionId, oneUse: false),
                CancellationToken.None)));

        outcomes.Should().OnlyContain(outcome => outcome == CampaignReservationOutcome.Reserved);
    }

    [Fact]
    public async Task A_released_slot_can_be_reclaimed_by_a_different_subscription()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        var discountId = Guid.NewGuid().ToString();
        var organizationId = "org-reclaim";
        var abandoned = Guid.NewGuid().ToString();
        var reclaimer = Guid.NewGuid().ToString();

        await repository.TryReserveAsync(
            Reservation(tenantId, organizationId, discountId, abandoned, oneUse: true),
            CancellationToken.None);

        // The abandoned subscription never activates -- IncompleteExpired -- and its slot is
        // given back.
        await repository.TryReleaseAsync(
            tenantId, discountId, abandoned, DateTime.UtcNow, CancellationToken.None);

        var reclaim = await repository.TryReserveAsync(
            Reservation(tenantId, organizationId, discountId, reclaimer, oneUse: true),
            CancellationToken.None);

        reclaim.Should().Be(CampaignReservationOutcome.Reserved);

        var abandonedRow = await repository.FindAsync(
            tenantId, discountId, abandoned, CancellationToken.None);
        abandonedRow!.State.Should().Be(CampaignRedemptionState.Released);
    }

    [Fact]
    public async Task Redeeming_is_idempotent_against_a_repeated_activation_event()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        var discountId = Guid.NewGuid().ToString();
        var subscriptionId = Guid.NewGuid().ToString();

        await repository.TryReserveAsync(
            Reservation(tenantId, "org-redeem", discountId, subscriptionId, oneUse: true),
            CancellationToken.None);

        var firstRedeemedAt = DateTime.UtcNow;
        await repository.TryMarkRedeemedAsync(
            tenantId, discountId, subscriptionId, firstRedeemedAt, CancellationToken.None);

        // A duplicate delivery of the same activation event, the way a payment webhook can arrive
        // twice. Must not move the timestamp or throw the second time.
        await repository.TryMarkRedeemedAsync(
            tenantId, discountId, subscriptionId, DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        var stored = await repository.FindAsync(tenantId, discountId, subscriptionId, CancellationToken.None);
        stored!.State.Should().Be(CampaignRedemptionState.Redeemed);
        stored.RedeemedAtUtc.Should().BeCloseTo(firstRedeemedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_redeemed_reservation_is_never_released_by_a_later_cancellation()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        var discountId = Guid.NewGuid().ToString();
        var subscriptionId = Guid.NewGuid().ToString();

        await repository.TryReserveAsync(
            Reservation(tenantId, "org-post-activation", discountId, subscriptionId, oneUse: true),
            CancellationToken.None);
        await repository.TryMarkRedeemedAsync(
            tenantId, discountId, subscriptionId, DateTime.UtcNow, CancellationToken.None);

        // A subscription that activated and was later cancelled must keep its campaign redeemed --
        // this is the call site a cancellation-after-activation would make, and it must be a no-op.
        await repository.TryReleaseAsync(
            tenantId, discountId, subscriptionId, DateTime.UtcNow, CancellationToken.None);

        var stored = await repository.FindAsync(tenantId, discountId, subscriptionId, CancellationToken.None);
        stored!.State.Should().Be(CampaignRedemptionState.Redeemed);
    }

    [Fact]
    public async Task Releasing_is_idempotent_against_a_repeated_call()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        var discountId = Guid.NewGuid().ToString();
        var subscriptionId = Guid.NewGuid().ToString();

        await repository.TryReserveAsync(
            Reservation(tenantId, "org-double-release", discountId, subscriptionId, oneUse: true),
            CancellationToken.None);

        await repository.TryReleaseAsync(
            tenantId, discountId, subscriptionId, DateTime.UtcNow, CancellationToken.None);
        await repository.TryReleaseAsync(
            tenantId, discountId, subscriptionId, DateTime.UtcNow, CancellationToken.None);

        var stored = await repository.FindAsync(tenantId, discountId, subscriptionId, CancellationToken.None);
        stored!.State.Should().Be(CampaignRedemptionState.Released);
    }

    private static CampaignRedemption Reservation(
        string tenantId, string organizationId, string discountId, string subscriptionId, bool oneUse) => new()
    {
        TenantId = tenantId,
        OrganizationId = organizationId,
        DiscountId = discountId,
        SubscriptionId = subscriptionId,
        OneUsePerOrganization = oneUse,
        CampaignVersion = 1,
        ReservedAtUtc = DateTime.UtcNow
    };
}
