using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StoredPaymentMethodRemovalRecoveryProcessorTests
{
    private readonly Mock<IStoredPaymentMethodRepository> _methods = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly Mock<IStoredPaymentMethodProviderGatewayResolver> _gatewayResolver = new();
    private readonly Mock<IStoredPaymentMethodProviderGateway> _gateway = new();
    private readonly Mock<IProviderTokenProtector> _tokenProtector = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();

    public StoredPaymentMethodRemovalRecoveryProcessorTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
        _tokenProtector.Setup(t => t.UnprotectAsync(It.IsAny<StoredPaymentMethod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderTokenReadResult(true, "token"));
        _gatewayResolver.Setup(r => r.Resolve("provider")).Returns(_gateway.Object);
        _providers.Setup(p => p.GetAsync("tenant", It.IsAny<string>(), "provider", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(new PaymentProvider { ProviderName = "provider", IsEnabled = true });
    }

    private StoredPaymentMethodRemovalRecoveryProcessor CreateService() => new(
        _methods.Object, _payments.Object, _providers.Object, _gatewayResolver.Object,
        _tokenProtector.Object, _options.Object, NullLogger<StoredPaymentMethodRemovalRecoveryProcessor>.Instance);

    private static StoredPaymentMethod Candidate(int attempts = 0) => new()
    {
        ItemId = "method-1",
        TenantId = "tenant",
        ProviderName = "provider",
        ProviderTokenCiphertext = "existing-cipher",
        RemovalAttemptCount = attempts
    };

    private void SetupCandidates(params StoredPaymentMethod[] candidates) =>
        _methods.Setup(m => m.GetDueRemovalCandidatesAsync("tenant", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates.ToList());

    private void SetupClaim(StoredPaymentMethod? claimed) =>
        _methods.Setup(m => m.TryClaimDueRemovalAsync("tenant", "method-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);

    private void SetupRemove(StoredPaymentMethodRemovalOutcome outcome) =>
        _gateway.Setup(g => g.RemoveAsync(It.IsAny<PaymentProvider>(), It.IsAny<StoredPaymentMethod>(), "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    [Fact]
    public async Task RecoverDueRemovalsAsync_NoCandidates_ReturnsZero()
    {
        SetupCandidates();

        var recovered = await CreateService().RecoverDueRemovalsAsync("tenant", CancellationToken.None);

        recovered.Should().Be(0);
    }

    [Fact]
    public async Task RecoverDueRemovalsAsync_ClaimNull_Skips()
    {
        SetupCandidates(Candidate());
        SetupClaim(null);

        var recovered = await CreateService().RecoverDueRemovalsAsync("tenant", CancellationToken.None);

        recovered.Should().Be(0);
        _gateway.Verify(g => g.RemoveAsync(It.IsAny<PaymentProvider>(), It.IsAny<StoredPaymentMethod>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecoverDueRemovalsAsync_TokenUnavailable_MarksOutcomeUnknown()
    {
        SetupCandidates(Candidate());
        SetupClaim(Candidate());
        _tokenProtector.Setup(t => t.UnprotectAsync(It.IsAny<StoredPaymentMethod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProviderTokenReadResult.Failed);

        var recovered = await CreateService().RecoverDueRemovalsAsync("tenant", CancellationToken.None);

        recovered.Should().Be(0);
        _methods.Verify(m => m.MarkRemovalOutcomeUnknownAsync("tenant", "method-1", It.IsAny<string>(), It.IsAny<DateTime>(), "provider_token_unavailable", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverDueRemovalsAsync_ProviderUnavailable_MarksFailure()
    {
        SetupCandidates(Candidate());
        SetupClaim(Candidate());
        _providers.Setup(p => p.GetAsync("tenant", It.IsAny<string>(), "provider", It.IsAny<Func<Task<PaymentProvider?>>>())).ReturnsAsync((PaymentProvider?)null);

        var recovered = await CreateService().RecoverDueRemovalsAsync("tenant", CancellationToken.None);

        recovered.Should().Be(0);
        _methods.Verify(m => m.MarkRemovalOutcomeUnknownAsync("tenant", "method-1", It.IsAny<string>(), It.IsAny<DateTime>(), "provider_unavailable", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverDueRemovalsAsync_Removed_IncrementsRecovered()
    {
        SetupCandidates(Candidate());
        SetupClaim(Candidate());
        SetupRemove(StoredPaymentMethodRemovalOutcome.Removed);
        _methods.Setup(m => m.MarkRemovedAsync("tenant", "method-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var recovered = await CreateService().RecoverDueRemovalsAsync("tenant", CancellationToken.None);

        recovered.Should().Be(1);
    }

    [Fact]
    public async Task RecoverDueRemovalsAsync_OperationalFailure_MarksFailure()
    {
        SetupCandidates(Candidate());
        SetupClaim(Candidate());
        SetupRemove(StoredPaymentMethodRemovalOutcome.OperationalFailure);

        var recovered = await CreateService().RecoverDueRemovalsAsync("tenant", CancellationToken.None);

        recovered.Should().Be(0);
        _methods.Verify(m => m.MarkRemovalOutcomeUnknownAsync("tenant", "method-1", It.IsAny<string>(), It.IsAny<DateTime>(), "provider_operational_failure", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverDueRemovalsAsync_ExhaustedAttempts_RequiresAttention()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions { StoredPaymentMethodRemovalMaxAttempts = 3 });
        SetupCandidates(Candidate());
        SetupClaim(Candidate(attempts: 2));
        SetupRemove(StoredPaymentMethodRemovalOutcome.OutcomeUnknown);

        var recovered = await CreateService().RecoverDueRemovalsAsync("tenant", CancellationToken.None);

        recovered.Should().Be(0);
        _methods.Verify(m => m.MarkRemovalRequiresAttentionAsync("tenant", "method-1", It.IsAny<string>(), "provider_outcome_unknown", It.IsAny<CancellationToken>()), Times.Once);
    }
}
