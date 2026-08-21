using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Finishing quantity increases whose caller died between reserving the units and settling the
/// charge.
/// </summary>
/// <remarks>
/// The reservation exists so neither half can be lost. These are the cases that prove it: money
/// taken and units owed, money never taken and a reservation to give back, and an authorization
/// still in flight that must be left alone rather than guessed at.
/// </remarks>
public sealed class SubscriptionQuantityClaimProcessorTests
{
    private const string TenantId = "tenant-1";
    private const string ClaimId = "claim-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

    private readonly SubscriptionDetail _subscription = new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Active,
        Version = 7,
        CurrencyCode = "CHF",
        QuantityItems = [Item(4)],
        QuantityChangeClaim = new QuantityChangeClaim
        {
            ClaimId = ClaimId,
            RequestedQuantities = [Item(5)],
            ChargeAmountMinor = 5_437,
            NewCreditBalanceMinor = 0,
            ClaimedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc),
            CorrelationId = "corr-1"
        }
    };

    public SubscriptionQuantityClaimProcessorTests()
    {
        _subscriptions
            .Setup(repository => repository.ListStaleQuantityClaimsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [_subscription]);

        _subscriptions
            .Setup(repository => repository.TryPromoteQuantityClaimAsync(
                TenantId, "sub-1", ClaimId,
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryReleaseQuantityClaimAsync(
                TenantId, "sub-1", ClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Theory]
    [InlineData(PaymentStatuses.Captured)]
    [InlineData(PaymentStatuses.Authorized)]
    [InlineData(PaymentStatuses.PartiallyCaptured)]
    public async Task A_settled_charge_grants_the_units_it_paid_for(string status)
    {
        GivenPayment(status);

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(1);
        _subscriptions.Verify(
            repository => repository.TryPromoteQuantityClaimAsync(
                TenantId, "sub-1", ClaimId,
                It.Is<List<SubscriptionQuantityItem>>(items => items.Single().Quantity == 5),
                0, "pay-1", It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_reservation_with_no_charge_behind_it_is_given_back()
    {
        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(1);
        _subscriptions.Verify(
            repository => repository.TryReleaseQuantityClaimAsync(
                TenantId, "sub-1", ClaimId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(PaymentStatuses.Refused)]
    [InlineData(PaymentStatuses.Cancelled)]
    [InlineData(PaymentStatuses.MakePaymentFailed)]
    public async Task A_charge_that_will_never_settle_gives_the_reservation_back(string status)
    {
        GivenPayment(status);

        await Processor().RecoverStaleAsync(TenantId, default);

        _subscriptions.Verify(
            repository => repository.TryReleaseQuantityClaimAsync(
                TenantId, "sub-1", ClaimId, It.IsAny<CancellationToken>()),
            Times.Once);
        _subscriptions.Verify(
            repository => repository.TryPromoteQuantityClaimAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(PaymentStatuses.Initiating)]
    [InlineData(PaymentStatuses.Processing)]
    public async Task An_authorization_still_in_flight_is_left_alone(string status)
    {
        GivenPayment(status);

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(0, "guessing either way here is how a subscriber loses paid units");
        _subscriptions.Verify(
            repository => repository.TryReleaseQuantityClaimAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _subscriptions.Verify(
            repository => repository.TryPromoteQuantityClaimAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task The_charge_is_looked_up_by_the_key_the_reservation_derived()
    {
        await Processor().RecoverStaleAsync(TenantId, default);

        // The one thing that makes an unrecorded charge findable at all.
        _payments.Verify(
            repository => repository.GetByIdempotencyKeyAsync(
                TenantId,
                SubscriptionConstants.QuantityChangeKeyFor("sub-1", ClaimId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void GivenPayment(string status) =>
        _payments
            .Setup(repository => repository.GetByIdempotencyKeyAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-1",
                TenantId = TenantId,
                PaymentStatus = status
            });

    private SubscriptionQuantityClaimProcessor Processor() => new(
        _subscriptions.Object,
        _payments.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<SubscriptionQuantityClaimProcessor>.Instance,
        _time);

    private static SubscriptionQuantityItem Item(long quantity) => new()
    {
        ItemKey = "user",
        UnitLabel = "user",
        Quantity = quantity,
        UnitAmountMinor = 14_500
    };
}
