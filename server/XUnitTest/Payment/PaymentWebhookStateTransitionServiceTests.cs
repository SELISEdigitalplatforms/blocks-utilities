using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentWebhookStateTransitionServiceTests
{
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IStoredPaymentMethodLifecycleService> _storedPaymentMethods = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _minorUnits = new();
    private readonly Mock<IPaymentRefundWebhookStateTransitionService> _refundTransitions = new();
    private readonly Mock<IPaymentCaptureWebhookStateTransitionService> _captureTransitions = new();
    private readonly Mock<IPaymentMethodSetupWebhookStateTransitionService> _setupTransitions = new();

    private PaymentWebhookStateTransitionService CreateService() => new(
        _payments.Object,
        _storedPaymentMethods.Object,
        new PaymentOutboxEventFactory(),
        _minorUnits.Object,
        _refundTransitions.Object,
        _captureTransitions.Object,
        _setupTransitions.Object,
        NullLogger<PaymentWebhookStateTransitionService>.Instance);

    private static PaymentWebhookInbox Webhook(
        WebhookIntent intent,
        string eventCode = "EVENT",
        string webhookType = "standard",
        PaymentWebhookPayload? payload = null) => new()
    {
        TenantId = "tenant",
        WebhookType = webhookType,
        EventCode = eventCode,
        Intent = intent,
        EventDateUtc = DateTime.UtcNow,
        NormalizedPayload = payload ?? new PaymentWebhookPayload()
    };

    [Fact]
    public async Task ApplyAsync_TokenWebhook_DelegatesToStoredPaymentMethods()
    {
        var webhook = Webhook(WebhookIntent.StoredMethod, "ANY", "token");

        await CreateService().ApplyAsync(webhook, CancellationToken.None);

        _storedPaymentMethods.Verify(s => s.ApplyTokenEventAsync(webhook, It.IsAny<CancellationToken>()), Times.Once);
        _refundTransitions.VerifyNoOtherCalls();
        _captureTransitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ApplyAsync_RefundIntent_DelegatesToRefundTransitions()
    {
        var webhook = Webhook(WebhookIntent.Refund);

        await CreateService().ApplyAsync(webhook, CancellationToken.None);

        _refundTransitions.Verify(r => r.ApplyAsync(webhook, It.IsAny<CancellationToken>()), Times.Once);
        _captureTransitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ApplyAsync_CaptureIntent_DelegatesToCaptureTransitions()
    {
        var webhook = Webhook(WebhookIntent.Capture);

        await CreateService().ApplyAsync(webhook, CancellationToken.None);

        _captureTransitions.Verify(c => c.ApplyAsync(webhook, It.IsAny<CancellationToken>()), Times.Once);
        _refundTransitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ApplyAsync_UnsupportedStandardEvent_IsSkipped()
    {
        var webhook = Webhook(WebhookIntent.Ignored, "SOMETHING_ELSE");

        await CreateService().ApplyAsync(webhook, CancellationToken.None);

        _payments.Verify(p => p.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _refundTransitions.VerifyNoOtherCalls();
        _captureTransitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ApplyAsync_IncompleteAuthorisation_Throws()
    {
        var webhook = Webhook(WebhookIntent.Authorization, payload: new PaymentWebhookPayload
        {
            PaymentDetailId = "pay-1",
            PspReference = null,
            Success = true
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().ApplyAsync(webhook, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_AuthorisationPaymentNotFound_Throws()
    {
        var webhook = Webhook(WebhookIntent.Authorization, payload: new PaymentWebhookPayload
        {
            PaymentDetailId = "pay-1",
            PspReference = "psp",
            Success = true,
            AmountMinorUnits = 1000,
            CurrencyCode = "EUR"
        });
        _payments.Setup(p => p.GetByIdAsync("tenant", "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().ApplyAsync(webhook, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_AuthorisationCurrencyConversionFails_Throws()
    {
        var webhook = Webhook(WebhookIntent.Authorization, payload: new PaymentWebhookPayload
        {
            PaymentDetailId = "pay-1",
            PspReference = "psp",
            Success = true,
            AmountMinorUnits = 1000,
            CurrencyCode = "EUR"
        });
        var payment = new PaymentDetail { ItemId = "pay-1", TenantId = "tenant", CurrencyCode = "EUR", PreciseAmount = 10 };
        _payments.Setup(p => p.GetByIdAsync("tenant", "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        long unused;
        _minorUnits.Setup(m => m.TryConvert(10, "EUR", out unused)).Returns(false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().ApplyAsync(webhook, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_AuthorisationAmountMismatch_Throws()
    {
        var webhook = Webhook(WebhookIntent.Authorization, payload: new PaymentWebhookPayload
        {
            PaymentDetailId = "pay-1",
            PspReference = "psp",
            Success = true,
            AmountMinorUnits = 999,
            CurrencyCode = "EUR"
        });
        var payment = new PaymentDetail { ItemId = "pay-1", TenantId = "tenant", CurrencyCode = "EUR", PreciseAmount = 10 };
        _payments.Setup(p => p.GetByIdAsync("tenant", "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        SetupConvert(10, "EUR", 1000);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().ApplyAsync(webhook, CancellationToken.None));
    }

    [Theory]
    [InlineData(true, PaymentCaptureModes.AutomaticImmediate, PaymentStatuses.Captured)]
    [InlineData(true, "MANUAL", PaymentStatuses.Authorized)]
    [InlineData(false, "MANUAL", PaymentStatuses.Refused)]
    public async Task ApplyAsync_AuthorisationApplied_TransitionsAndSyncsToken(bool success, string captureMode, string expectedStatus)
    {
        var webhook = Webhook(WebhookIntent.Authorization, payload: new PaymentWebhookPayload
        {
            PaymentDetailId = "pay-1",
            PspReference = "psp",
            Success = success,
            AmountMinorUnits = 1000,
            CurrencyCode = "EUR",
            PaymentMethodType = "scheme",
            Brand = "visa",
            LastFour = "4242"
        });
        var payment = new PaymentDetail
        {
            ItemId = "pay-1",
            TenantId = "tenant",
            CurrencyCode = "EUR",
            PreciseAmount = 10,
            CaptureMode = captureMode
        };
        _payments.Setup(p => p.GetByIdAsync("tenant", "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        SetupConvert(10, "EUR", 1000);
        var capturedAutomatically = captureMode == PaymentCaptureModes.AutomaticImmediate;
        _payments.Setup(p => p.ApplyAuthorisationAsync(
                "tenant",
                "pay-1",
                success,
                10,
                capturedAutomatically,
                "psp",
                It.IsAny<DateTime>(),
                It.Is<PaymentInstrument>(i => i.Brand == "visa" && i.LastFour == "4242"),
                It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Verifiable();

        await CreateService().ApplyAsync(webhook, CancellationToken.None);

        _payments.Verify();
        _storedPaymentMethods.Verify(s => s.ApplyAuthorisationTokenAsync(webhook, payment, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A provider that reports authorisation and capture through the same event is the only
    /// thing that knows which happened, including when the capture was made outside this
    /// service. Deciding from the configured mode recorded such a capture as merely
    /// authorised; deciding from the event records it as captured.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ApplyAsync_ManualCapture_FollowsWhatTheProviderReported(
        bool fundsCaptured,
        bool expectedCaptured)
    {
        var webhook = Webhook(WebhookIntent.Authorization, payload: new PaymentWebhookPayload
        {
            PaymentDetailId = "pay-1",
            PspReference = "psp",
            Success = true,
            FundsCaptured = fundsCaptured,
            AmountMinorUnits = 1000,
            CurrencyCode = "EUR",
            PaymentMethodType = "scheme"
        });
        var payment = new PaymentDetail
        {
            ItemId = "pay-1",
            TenantId = "tenant",
            CurrencyCode = "EUR",
            PreciseAmount = 10,
            CaptureMode = "MANUAL"
        };
        _payments.Setup(p => p.GetByIdAsync("tenant", "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        SetupConvert(10, "EUR", 1000);
        _payments.Setup(p => p.ApplyAuthorisationAsync(
                "tenant", "pay-1", true, 10, expectedCaptured, "psp",
                It.IsAny<DateTime>(), It.IsAny<PaymentInstrument>(),
                It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Verifiable();

        await CreateService().ApplyAsync(webhook, CancellationToken.None);

        _payments.Verify();
    }

    private void SetupConvert(decimal amount, string currency, long minorUnits)
    {
        _minorUnits.Setup(m => m.TryConvert(amount, currency, out It.Ref<long>.IsAny))
            .Callback(new TryConvertCallback((decimal _, string _, out long value) => value = minorUnits))
            .Returns(true);
    }

    private delegate void TryConvertCallback(decimal amount, string currency, out long value);
}
