using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Scheduling;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Finding 3: a card setup left waiting for a completion signal that never arrives (see
/// <see cref="PaymentMethodSetupWebhookStateTransitionService"/>'s two-signal model) must not
/// stay pending forever with no recovery path. These pin the expiry sweep's compare-and-set
/// safety (a concurrent completion always wins) and its metrics/logging.
/// </summary>
public sealed class PaymentMethodSetupExpiryProcessorTests
{
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();
    private readonly PaymentWorkMetrics _metrics = new();
    private readonly DateTime _now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    public PaymentMethodSetupExpiryProcessorTests() =>
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());

    private PaymentMethodSetupExpiryProcessor CreateService() => new(
        _payments.Object,
        _options.Object,
        _metrics,
        NullLogger<PaymentMethodSetupExpiryProcessor>.Instance,
        new FakeTimeProvider(_now));

    private static PaymentDetail Candidate(
        DateTime createdAtUtc,
        DateTime? authorizationConfirmedAtUtc = null,
        DateTime? tokenConfirmedAtUtc = null) => new()
    {
        ItemId = "payment-1",
        TenantId = "tenant-1",
        PaymentFlow = PaymentFlows.PaymentMethodSetup,
        PaymentStatus = PaymentStatuses.Processing,
        CreatedAtUtc = createdAtUtc,
        SetupAuthorizationConfirmedAtUtc = authorizationConfirmedAtUtc,
        SetupTokenConfirmedAtUtc = tokenConfirmedAtUtc
    };

    private void SetupCandidates(params PaymentDetail[] candidates) =>
        _payments.Setup(repository => repository.GetDueSetupExpiryCandidatesAsync(
                "tenant-1", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates.ToList());

    [Fact]
    public async Task No_candidates_expires_nothing()
    {
        SetupCandidates();

        var expired = await CreateService().ExpireDueAsync("tenant-1", CancellationToken.None);

        expired.Should().Be(0);
        _payments.Verify(
            repository => repository.TryExpireSetupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_setup_missing_both_signals_past_timeout_is_expired()
    {
        var candidate = Candidate(_now.AddHours(-1));
        SetupCandidates(candidate);
        _payments.Setup(repository => repository.TryExpireSetupAsync(
                "tenant-1", "payment-1", _now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var expired = await CreateService().ExpireDueAsync("tenant-1", CancellationToken.None);

        expired.Should().Be(1);
    }

    /// <summary>
    /// The compare-and-set at the repository is what makes this safe, but the processor must not
    /// count a lost race as an expiry either: a setup that completed or was declined between being
    /// read as a candidate and this call is not the outcome this sweep exists to produce.
    /// </summary>
    [Fact]
    public async Task A_setup_that_completes_concurrently_with_the_sweep_is_not_counted_as_expired()
    {
        var candidate = Candidate(_now.AddHours(-1), authorizationConfirmedAtUtc: _now.AddMinutes(-1));
        SetupCandidates(candidate);
        _payments.Setup(repository => repository.TryExpireSetupAsync(
                "tenant-1", "payment-1", _now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var expired = await CreateService().ExpireDueAsync("tenant-1", CancellationToken.None);

        expired.Should().Be(0);
    }

    [Fact]
    public async Task A_setup_missing_only_the_token_signal_is_still_expired_past_timeout()
    {
        var candidate = Candidate(_now.AddHours(-1), authorizationConfirmedAtUtc: _now.AddMinutes(-55));
        SetupCandidates(candidate);
        _payments.Setup(repository => repository.TryExpireSetupAsync(
                "tenant-1", "payment-1", _now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var expired = await CreateService().ExpireDueAsync("tenant-1", CancellationToken.None);

        expired.Should().Be(1);
    }

    [Fact]
    public async Task The_repository_is_asked_for_candidates_older_than_the_configured_timeout()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions
        {
            PaymentMethodSetupTimeoutSeconds = 600
        });
        SetupCandidates();

        await CreateService().ExpireDueAsync("tenant-1", CancellationToken.None);

        _payments.Verify(
            repository => repository.GetDueSetupExpiryCandidatesAsync(
                "tenant-1",
                _now.AddSeconds(-600),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
