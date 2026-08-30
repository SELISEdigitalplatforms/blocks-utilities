using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Finishing a campaign redemption a crash left stuck between a subscription's own transition and
/// the ledger's paired call -- the one remaining gap
/// <see cref="ICampaignRedemptionRepository.TryReleaseAsync"/>'s own ReleasePending step does not
/// already close on its own.
/// </summary>
public sealed class CampaignRedemptionReconcilerTests
{
    private const string TenantId = "tenant-1";
    private const string DiscountId = "discount-1";
    private const string SubscriptionId = "sub-1";

    private readonly Mock<ICampaignRedemptionRepository> _redemptions = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));

    public CampaignRedemptionReconcilerTests()
    {
        _redemptions
            .Setup(repository => repository.ListStaleAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([StaleRedemption()]);
    }

    [Fact]
    public async Task An_activated_subscriptions_stuck_reservation_is_marked_redeemed_at_its_own_activation_instant()
    {
        var activatedAtUtc = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
        GivenSubscription(new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            Status = SubscriptionStatus.Active,
            ActivatedAtUtc = activatedAtUtc
        });

        var resolved = await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        resolved.Should().Be(1);
        _redemptions.Verify(
            repository => repository.TryMarkRedeemedAsync(
                TenantId, DiscountId, SubscriptionId, activatedAtUtc, It.IsAny<CancellationToken>()),
            Times.Once);
        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A subscription that later cancelled is still redeemed, not released -- ActivatedAtUtc is
    /// never cleared, which is exactly what makes it the right signal instead of current status.
    /// </summary>
    [Fact]
    public async Task An_activated_subscription_that_later_cancelled_is_still_reconciled_as_redeemed()
    {
        GivenSubscription(new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            Status = SubscriptionStatus.Canceled,
            ActivatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryMarkRedeemedAsync(
                TenantId, DiscountId, SubscriptionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_never_activated_expired_subscriptions_reservation_is_released()
    {
        GivenSubscription(new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            Status = SubscriptionStatus.IncompleteExpired,
            ActivatedAtUtc = null
        });

        var resolved = await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        resolved.Should().Be(1);
        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                TenantId, DiscountId, SubscriptionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _redemptions.Verify(
            repository => repository.TryMarkRedeemedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Cancelled while still Incomplete -- the other way a subscription can end without ever
    /// activating, mirroring SubscriptionCancellationService.EndNowAsync's own condition exactly.
    /// </summary>
    [Fact]
    public async Task A_never_activated_cancelled_subscriptions_reservation_is_released()
    {
        GivenSubscription(new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            Status = SubscriptionStatus.Canceled,
            ActivatedAtUtc = null
        });

        await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                TenantId, DiscountId, SubscriptionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Resumes a row already at ReleasePending exactly the way it resumes one still at Reserved --
    /// TryReleaseAsync's own idempotence carries the difference, not anything read here.
    /// </summary>
    [Fact]
    public async Task A_row_already_at_release_pending_is_finished_the_same_way()
    {
        _redemptions
            .Setup(repository => repository.ListStaleAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([StaleRedemption(CampaignRedemptionState.ReleasePending)]);
        GivenSubscription(new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            Status = SubscriptionStatus.Canceled,
            ActivatedAtUtc = null
        });

        await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                TenantId, DiscountId, SubscriptionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_subscription_still_incomplete_and_undecided_is_left_alone()
    {
        GivenSubscription(new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            Status = SubscriptionStatus.Incomplete,
            ActivatedAtUtc = null
        });

        var resolved = await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        resolved.Should().Be(0);
        _redemptions.Verify(
            repository => repository.TryMarkRedeemedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_redemption_naming_a_subscription_that_cannot_be_found_is_left_alone()
    {
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        var resolved = await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        resolved.Should().Be(0);
    }

    [Fact]
    public async Task The_grace_period_is_read_from_options_rather_than_hardcoded()
    {
        DateTime? requestedThreshold = null;
        _redemptions
            .Setup(repository => repository.ListStaleAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, DateTime, int, CancellationToken>((_, threshold, _, _) => requestedThreshold = threshold)
            .ReturnsAsync([]);

        var options = new SubscriptionOptionsMonitorStub(new SubscriptionOptions
        {
            CampaignRedemptionGraceMinutes = 45
        });

        await new CampaignRedemptionReconciler(
                _redemptions.Object, _subscriptions.Object, options,
                NullLogger<CampaignRedemptionReconciler>.Instance, _time)
            .ReconcileAsync(TenantId, CancellationToken.None);

        requestedThreshold.Should().Be(_time.GetUtcNow().UtcDateTime.AddMinutes(-45));
    }

    private void GivenSubscription(SubscriptionDetail subscription) =>
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

    private CampaignRedemptionReconciler Reconciler() => new(
        _redemptions.Object,
        _subscriptions.Object,
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<CampaignRedemptionReconciler>.Instance,
        _time);

    private static CampaignRedemption StaleRedemption(
        CampaignRedemptionState state = CampaignRedemptionState.Reserved) => new()
    {
        TenantId = TenantId,
        OrganizationId = "org-1",
        DiscountId = DiscountId,
        SubscriptionId = SubscriptionId,
        State = state,
        ReservedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
