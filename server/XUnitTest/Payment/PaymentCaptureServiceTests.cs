using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentCaptureServiceTests
{
    private readonly Mock<IPaymentExecutionContextResolver> _contextResolver = new();
    private readonly Mock<IPaymentCapturePreflightService> _preflight = new();
    private readonly Mock<IPaymentDistributedLock> _distributedLock = new();
    private readonly Mock<IPaymentCaptureReservationService> _reservations = new();
    private readonly Mock<IPaymentCaptureInitiationService> _initiation = new();
    private readonly Mock<IPaymentCaptureRepository> _captures = new();
    private readonly Mock<IPaymentCaptureResponseMapper> _responses = new();

    private readonly PaymentExecutionContext _context = new("tenant", "actor", null);
    private readonly CreatePaymentCaptureRequest _request = new() { Amount = 10 };
    private const string PaymentId = "pay-1";

    public PaymentCaptureServiceTests()
    {
        _contextResolver.Setup(c => c.Resolve(It.IsAny<string>())).Returns(new PaymentContextResolution(_context, null));
        _distributedLock.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IPaymentLockHandle?)null);
    }

    private PaymentCaptureService CreateService() => new(
        _contextResolver.Object, _preflight.Object, _distributedLock.Object,
        _reservations.Object, _initiation.Object, _captures.Object, _responses.Object);

    private void SetupContextFailure() =>
        _contextResolver.Setup(c => c.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(null, PaymentOperationResult.Failure(PaymentFailureKind.Validation, "unauthorized", "no", "corr")));

    private void SetupPreflight(PaymentCapturePreflightResult result) =>
        _preflight.Setup(p => p.ExecuteAsync(PaymentId, _request, It.IsAny<string>(), _context, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private Task<PaymentCaptureOperationResult> CreateAsync() =>
        CreateService().CreatePaymentCaptureAsync(PaymentId, _request, Guid.NewGuid().ToString(), "corr", CancellationToken.None);

    [Fact]
    public async Task CreatePaymentCaptureAsync_ContextFails_ReturnsFailure()
    {
        SetupContextFailure();

        var result = await CreateAsync();

        result.ErrorCode.Should().Be("unauthorized");
    }

    [Fact]
    public async Task CreatePaymentCaptureAsync_PreflightFails_ReturnsPreflightFailure()
    {
        var failure = PaymentCaptureOperationResult.Failure(PaymentFailureKind.NotFound, "payment_not_found", "no", "corr");
        SetupPreflight(new PaymentCapturePreflightResult(0, null, null, null, failure));

        var result = await CreateAsync();

        result.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task CreatePaymentCaptureAsync_ReservationCannotSubmit_ReturnsTerminalWithRateLimit()
    {
        var rateLimit = new PaymentRateLimitResult { Limit = 5 };
        SetupPreflight(new PaymentCapturePreflightResult(1000, rateLimit, new PaymentDetail { ItemId = PaymentId }, new PaymentProvider(), null));
        var terminal = PaymentCaptureOperationResult.Failure(PaymentFailureKind.Conflict, "idempotency_key_reused", "no", "corr");
        _reservations.Setup(r => r.ReserveAsync(It.IsAny<PaymentDetail>(), It.IsAny<PaymentProvider>(), _request, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentCaptureReservationResult(null, null, null, terminal));

        var result = await CreateAsync();

        result.ErrorCode.Should().Be("idempotency_key_reused");
        result.RateLimit.Should().Be(rateLimit);
    }

    [Fact]
    public async Task CreatePaymentCaptureAsync_HappyPath_ReturnsInitiationResultWithRateLimit()
    {
        var rateLimit = new PaymentRateLimitResult { Limit = 7 };
        SetupPreflight(new PaymentCapturePreflightResult(1000, rateLimit, new PaymentDetail { ItemId = PaymentId }, new PaymentProvider(), null));
        var capture = new PaymentCapture { CaptureId = "cap-1" };
        _reservations.Setup(r => r.ReserveAsync(It.IsAny<PaymentDetail>(), It.IsAny<PaymentProvider>(), _request, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentCaptureReservationResult(new PaymentDetail { ItemId = PaymentId }, capture, "lease", null));
        _initiation.Setup(i => i.SubmitAsync(It.IsAny<PaymentDetail>(), capture, It.IsAny<PaymentProvider>(), "lease", 1000, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentCaptureOperationResult.Success(new PaymentCaptureResponse(), "corr"));

        var result = await CreateAsync();

        result.IsSuccess.Should().BeTrue();
        result.RateLimit.Should().Be(rateLimit);
    }

    [Fact]
    public async Task GetPaymentCaptureAsync_ContextFails_ReturnsFailure()
    {
        SetupContextFailure();

        var result = await CreateService().GetPaymentCaptureAsync(PaymentId, "cap-1", "corr", CancellationToken.None);

        result.ErrorCode.Should().Be("unauthorized");
    }

    [Fact]
    public async Task GetPaymentCaptureAsync_NotFound_ReturnsNotFound()
    {
        _captures.Setup(c => c.GetPaymentByCaptureIdAsync("tenant", "cap-1", It.IsAny<CancellationToken>())).ReturnsAsync((PaymentDetail?)null);

        var result = await CreateService().GetPaymentCaptureAsync(PaymentId, "cap-1", "corr", CancellationToken.None);

        result.ErrorCode.Should().Be("payment_capture_not_found");
    }

    [Fact]
    public async Task GetPaymentCaptureAsync_Found_ReturnsSuccess()
    {
        var capture = new PaymentCapture { CaptureId = "cap-1" };
        var payment = new PaymentDetail { ItemId = PaymentId, Captures = new List<PaymentCapture> { capture } };
        _captures.Setup(c => c.GetPaymentByCaptureIdAsync("tenant", "cap-1", It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _responses.Setup(m => m.Map(PaymentId, capture)).Returns(new PaymentCaptureResponse());

        var result = await CreateService().GetPaymentCaptureAsync(PaymentId, "cap-1", "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
