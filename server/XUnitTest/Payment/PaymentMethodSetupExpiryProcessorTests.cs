using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
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
        new PaymentOutboxEventFactory(),
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

    /// <summary>
    /// Finding 1's residual completion gap: a setup that already has both signals recorded but is
    /// still Processing -- the case where a process crashed between recording the final signal
    /// and calling <see cref="PaymentMethodSetupCompletion"/>, with no further webhook redelivery
    /// left to retry it -- must still be completed by the reconciliation sweep rather than left
    /// stuck forever (or, after Finding 1's CAS fix, correctly refused expiry but never finished
    /// either).
    /// </summary>
    [Fact]
    public async Task A_processing_setup_with_both_signals_already_recorded_is_completed_by_the_sweep()
    {
        var readyButStuck = Candidate(
            _now.AddMinutes(-5),
            authorizationConfirmedAtUtc: _now.AddMinutes(-2),
            tokenConfirmedAtUtc: _now.AddMinutes(-1));
        _payments.SetupSequence(repository => repository.GetSetupsReadyForCompletionAsync(
                "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([readyButStuck])
            .ReturnsAsync([]);
        SetupCandidates();
        _payments.Setup(repository => repository.ApplyAuthorisationAsync(
                "tenant-1",
                "payment-1",
                true,
                0m,
                false,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await CreateService().ExpireDueAsync("tenant-1", CancellationToken.None);

        _payments.Verify(
            repository => repository.ApplyAuthorisationAsync(
                "tenant-1",
                "payment-1",
                true,
                0m,
                false,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            repository => repository.TryExpireSetupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Finding 3: the pending-age metric must be observable for a setup still comfortably within
    /// its timeout, not only once it is already due for expiry -- otherwise it can never show age
    /// climbing over time, only ever a setup already at the cliff edge. This does not assert on
    /// the metric's recorded value directly (no test seam for that here); it pins that the
    /// aggregation-based summary is asked for on every sweep, and that asking for it does not
    /// itself expire or complete anything.
    /// </summary>
    [Fact]
    public async Task A_setup_well_within_its_timeout_is_still_examined_for_pending_age_but_left_alone()
    {
        _payments.Setup(repository => repository.GetPendingSetupAgeSummaryAsync(
                "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new List<PendingSetupAgeSummary>
                {
                    new("authorization", 1, _now.AddSeconds(-30))
                });
        SetupCandidates();

        await CreateService().ExpireDueAsync("tenant-1", CancellationToken.None);

        _payments.Verify(
            repository => repository.GetPendingSetupAgeSummaryAsync(
                "tenant-1", It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            repository => repository.TryExpireSetupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            repository => repository.ApplyAuthorisationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<decimal>(),
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<DateTime>(), null,
                It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// PR #393 review (Finding, round 5): the two concerns that used to share one capped query
    /// must run as genuinely independent passes. This pins the completion side of that split: a
    /// tenant can have more ready-to-complete setups than fit in a single batch, and every one of
    /// them must still be completed within the same sweep rather than only the first batch's
    /// worth -- proving the ready-to-complete path is no longer capped the way the old shared
    /// query was.
    /// </summary>
    [Fact]
    public async Task Every_ready_setup_is_completed_even_when_more_than_one_batch_worth_exists()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions { WebhookBatchSize = 1 });

        var first = Candidate(
            _now.AddMinutes(-5),
            authorizationConfirmedAtUtc: _now.AddMinutes(-2),
            tokenConfirmedAtUtc: _now.AddMinutes(-1));
        var second = Candidate(
            _now.AddMinutes(-4),
            authorizationConfirmedAtUtc: _now.AddMinutes(-2),
            tokenConfirmedAtUtc: _now.AddMinutes(-1));
        second.ItemId = "payment-2";

        _payments.SetupSequence(repository => repository.GetSetupsReadyForCompletionAsync(
                "tenant-1", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first])
            .ReturnsAsync([second])
            .ReturnsAsync([]);
        SetupCandidates();
        _payments.Setup(repository => repository.ApplyAuthorisationAsync(
                "tenant-1",
                It.IsAny<string>(),
                true,
                0m,
                false,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await CreateService().ExpireDueAsync("tenant-1", CancellationToken.None);

        _payments.Verify(
            repository => repository.ApplyAuthorisationAsync(
                "tenant-1", "payment-1", true, 0m, false, It.IsAny<string>(), It.IsAny<DateTime>(),
                null, It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            repository => repository.ApplyAuthorisationAsync(
                "tenant-1", "payment-2", true, 0m, false, It.IsAny<string>(), It.IsAny<DateTime>(),
                null, It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            repository => repository.GetSetupsReadyForCompletionAsync(
                "tenant-1", 1, It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
