using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class PaymentRefundRecoveryProcessorTests
{
    private readonly Mock<IPaymentRefundRepository> _refunds = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _minorUnits = new();
    private readonly Mock<IPaymentRefundInitiationService> _initiation = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();

    public PaymentRefundRecoveryProcessorTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
    }

    private PaymentRefundRecoveryProcessor CreateService() => new(
        _refunds.Object, _payments.Object, _providers.Object, _minorUnits.Object,
        _initiation.Object, _options.Object, NullLogger<PaymentRefundRecoveryProcessor>.Instance);

    private static PaymentRefund DueRefund(int attemptCount = 0) => new()
    {
        RefundId = "ref-1",
        ProviderName = "provider",
        Amount = 10,
        CurrencyCode = "EUR",
        Status = PaymentRefundStatuses.Initiating,
        InitiationAttemptCount = attemptCount,
        NextRecoveryAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
    };

    private static PaymentDetail PaymentWith(PaymentRefund refund) => new()
    {
        ItemId = "pay-1",
        TenantId = "tenant",
        Refunds = new List<PaymentRefund> { refund }
    };

    private void SetupDue(params PaymentDetail[] payments) =>
        _refunds.Setup(r => r.GetPaymentsWithDueRefundInitiationsAsync("tenant", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments.ToList());

    private void SetupClaim(PaymentRefund? claimed) =>
        _refunds.Setup(r => r.TryClaimInitiationAsync("tenant", "pay-1", "ref-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions { RefundRecoveryMaxAttempts = 3 });
        SetupDue(PaymentWith(DueRefund(attemptCount: 3)));

        var processed = await CreateService().RecoverDueAsync("tenant", CancellationToken.None);

        processed.Should().Be(1);
        _refunds.Verify(r => r.MarkRequiresAttentionAsync("tenant", "pay-1", "ref-1", null, "payment_refund_recovery_exhausted", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverDueAsync_ClaimNull_Skips()
    {
        SetupDue(PaymentWith(DueRefund()));
        SetupClaim(null);

        var processed = await CreateService().RecoverDueAsync("tenant", CancellationToken.None);

        processed.Should().Be(0);
        _initiation.Verify(i => i.SubmitAsync(It.IsAny<PaymentDetail>(), It.IsAny<PaymentRefund>(), It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecoverDueAsync_ProviderUnavailable_MarksRequiresAttention()
    {
        SetupDue(PaymentWith(DueRefund()));
        SetupClaim(DueRefund());
        _providers.Setup(p => p.GetAsync("tenant", "provider", It.IsAny<Func<Task<PaymentProvider?>>>())).ReturnsAsync((PaymentProvider?)null);

        var processed = await CreateService().RecoverDueAsync("tenant", CancellationToken.None);

        processed.Should().Be(1);
        _refunds.Verify(r => r.MarkRequiresAttentionAsync("tenant", "pay-1", "ref-1", It.IsAny<string>(), "payment_refund_recovery_unavailable", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverDueAsync_HappyPath_Resubmits()
    {
        SetupDue(PaymentWith(DueRefund()));
        var claimed = DueRefund();
        SetupClaim(claimed);
        _providers.Setup(p => p.GetAsync("tenant", "provider", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(new PaymentProvider { ProviderName = "provider", IsEnabled = true });
        SetupConvert(true, 1000);
        _initiation.Setup(i => i.SubmitAsync(It.IsAny<PaymentDetail>(), claimed, It.IsAny<PaymentProvider>(), It.IsAny<string>(), 1000, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRefundOperationResult.Success(new PaymentRefundResponse(), "corr"));

        var processed = await CreateService().RecoverDueAsync("tenant", CancellationToken.None);

        processed.Should().Be(1);
        _initiation.Verify(i => i.SubmitAsync(It.IsAny<PaymentDetail>(), claimed, It.IsAny<PaymentProvider>(), It.IsAny<string>(), 1000, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private delegate void TryConvertCallback(decimal amount, string currency, out long value);
}
