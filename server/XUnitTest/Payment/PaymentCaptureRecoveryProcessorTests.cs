using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentCaptureRecoveryProcessorTests
{
    private readonly Mock<IPaymentCaptureRepository> _captures = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _minorUnits = new();
    private readonly Mock<IPaymentCaptureInitiationService> _initiation = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();

    public PaymentCaptureRecoveryProcessorTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
    }

    private PaymentCaptureRecoveryProcessor CreateService() => new(
        _captures.Object, _payments.Object, _providers.Object, _minorUnits.Object,
        _initiation.Object, _options.Object);

    private static PaymentCapture DueCapture(int attempts = 0) => new()
    {
        CaptureId = "cap-1",
        ProviderName = "provider",
        Amount = 10,
        CurrencyCode = "EUR",
        Status = PaymentCaptureStatuses.Initiating,
        InitiationAttemptCount = attempts,
        NextRecoveryAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
    };

    private static PaymentDetail PaymentWith(PaymentCapture capture) => new()
    {
        ItemId = "pay-1",
        TenantId = "tenant",
        Captures = new List<PaymentCapture> { capture }
    };

    private void SetupDue(params PaymentDetail[] payments) =>
        _captures.Setup(c => c.GetPaymentsWithDueCaptureInitiationsAsync("tenant", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments.ToList());

    private void SetupClaim(PaymentCapture? claimed) =>
        _captures.Setup(c => c.TryClaimInitiationAsync("tenant", "pay-1", "cap-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);

    private void SetupConvert(bool ok, long minorUnits = 1000) =>
        _minorUnits.Setup(m => m.TryConvert(It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<long>.IsAny))
            .Callback(new TryConvertCallback((decimal _, string _, out long value) => value = minorUnits))
            .Returns(ok);

    [Fact]
    public async Task RecoverDueAsync_NoPayments_ReturnsZero()
    {
        SetupDue();

        var processed = await CreateService().RecoverDueAsync("tenant", CancellationToken.None);

        processed.Should().Be(0);
    }

    [Fact]
    public async Task RecoverDueAsync_AttemptsExhausted_MarksRequiresAttention()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions { CaptureRecoveryMaxAttempts = 3 });
        SetupDue(PaymentWith(DueCapture(attempts: 3)));

        var processed = await CreateService().RecoverDueAsync("tenant", CancellationToken.None);

        processed.Should().Be(1);
        _captures.Verify(c => c.MarkRequiresAttentionAsync("tenant", "pay-1", "cap-1", null, "payment_capture_recovery_exhausted", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverDueAsync_ClaimNull_Skips()
    {
        SetupDue(PaymentWith(DueCapture()));
        SetupClaim(null);

        var processed = await CreateService().RecoverDueAsync("tenant", CancellationToken.None);

        processed.Should().Be(0);
        _initiation.Verify(i => i.SubmitAsync(It.IsAny<PaymentDetail>(), It.IsAny<PaymentCapture>(), It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecoverDueAsync_ProviderUnavailable_MarksRequiresAttention()
    {
        SetupDue(PaymentWith(DueCapture()));
        SetupClaim(DueCapture());
        _providers.Setup(p => p.GetAsync("tenant", It.IsAny<string>(), "provider", It.IsAny<Func<Task<PaymentProvider?>>>())).ReturnsAsync((PaymentProvider?)null);

        var processed = await CreateService().RecoverDueAsync("tenant", CancellationToken.None);

        processed.Should().Be(1);
        _captures.Verify(c => c.MarkRequiresAttentionAsync("tenant", "pay-1", "cap-1", It.IsAny<string>(), "payment_capture_recovery_unavailable", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverDueAsync_HappyPath_Resubmits()
    {
        SetupDue(PaymentWith(DueCapture()));
        var claimed = DueCapture();
        SetupClaim(claimed);
        _providers.Setup(p => p.GetAsync("tenant", It.IsAny<string>(), "provider", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(new PaymentProvider { ProviderName = "provider", IsEnabled = true });
        SetupConvert(true, 1000);
        _initiation.Setup(i => i.SubmitAsync(It.IsAny<PaymentDetail>(), claimed, It.IsAny<PaymentProvider>(), It.IsAny<string>(), 1000, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentCaptureOperationResult.Success(new PaymentCaptureResponse(), "corr"));

        var processed = await CreateService().RecoverDueAsync("tenant", CancellationToken.None);

        processed.Should().Be(1);
        _initiation.Verify(i => i.SubmitAsync(It.IsAny<PaymentDetail>(), claimed, It.IsAny<PaymentProvider>(), It.IsAny<string>(), 1000, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private delegate void TryConvertCallback(decimal amount, string currency, out long value);
}
