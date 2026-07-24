using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentRefundServiceTests
{
    private readonly Mock<IPaymentExecutionContextResolver> _contextResolver = new();
    private readonly Mock<IPaymentRefundPreflightService> _preflight = new();
    private readonly Mock<IPaymentDistributedLock> _distributedLock = new();
    private readonly Mock<IPaymentRefundReservationService> _reservations = new();
    private readonly Mock<IPaymentRefundInitiationService> _initiation = new();
    private readonly Mock<IPaymentRefundRepository> _refunds = new();
    private readonly Mock<IPaymentRefundResponseMapper> _responses = new();

    private readonly PaymentExecutionContext _context = new("tenant", "actor", null);
    private readonly CreatePaymentRefundRequest _request = new() { Amount = 10 };
    private const string PaymentId = "pay-1";

    public PaymentRefundServiceTests()
    {
        _contextResolver.Setup(c => c.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(_context, null));
        _distributedLock.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPaymentLockHandle?)null);
    }

    private PaymentRefundService CreateService() => new(
        _contextResolver.Object, _preflight.Object, _distributedLock.Object,
        _reservations.Object, _initiation.Object, _refunds.Object, _responses.Object);

    private void SetupContextFailure() =>
        _contextResolver.Setup(c => c.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(null, PaymentOperationResult.Failure(PaymentFailureKind.Validation, "unauthorized", "no", "corr")));

    private void SetupPreflight(PaymentRefundPreflightResult result) =>
        _preflight.Setup(p => p.ExecuteAsync(PaymentId, _request, It.IsAny<string>(), _context, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private static PaymentRefundPreflightResult PreflightSuccess() =>
        new(1000, "refund", new PaymentRateLimitResult(), new PaymentDetail { ItemId = PaymentId }, new PaymentProvider(), null);

    private Task<PaymentRefundOperationResult> CreateAsync() =>
        CreateService().CreatePaymentRefundAsync(PaymentId, _request, Guid.NewGuid().ToString(), "corr", CancellationToken.None);

    [Fact]
    public async Task CreatePaymentRefundAsync_ContextFails_ReturnsFailure()
    {
        SetupContextFailure();

        var result = await CreateAsync();

        result.ErrorCode.Should().Be("unauthorized");
    }

    [Fact]
    public async Task CreatePaymentRefundAsync_PreflightFails_ReturnsPreflightFailure()
    {
        var failure = PaymentRefundOperationResult.Failure(PaymentFailureKind.NotFound, "payment_not_found", "no", "corr");
        SetupPreflight(new PaymentRefundPreflightResult(0, string.Empty, null, null, null, failure));

        var result = await CreateAsync();

        result.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task CreatePaymentRefundAsync_ReservationCannotSubmit_ReturnsTerminalWithRateLimit()
    {
        var rateLimit = new PaymentRateLimitResult { Limit = 5 };
        SetupPreflight(new PaymentRefundPreflightResult(1000, "refund", rateLimit, new PaymentDetail { ItemId = PaymentId }, new PaymentProvider(), null));
        var terminal = PaymentRefundOperationResult.Failure(PaymentFailureKind.Conflict, "idempotency_key_reused", "no", "corr");
        _reservations.Setup(r => r.ReserveAsync(It.IsAny<PaymentDetail>(), It.IsAny<PaymentProvider>(), _request, "refund", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRefundReservationResult(null, null, null, terminal));

        var result = await CreateAsync();

        result.ErrorCode.Should().Be("idempotency_key_reused");
        result.RateLimit.Should().Be(rateLimit);
    }

    [Fact]
    public async Task CreatePaymentRefundAsync_HappyPath_ReturnsInitiationResultWithRateLimit()
    {
        var rateLimit = new PaymentRateLimitResult { Limit = 7 };
        SetupPreflight(new PaymentRefundPreflightResult(1000, "refund", rateLimit, new PaymentDetail { ItemId = PaymentId }, new PaymentProvider(), null));
        var refund = new PaymentRefund { RefundId = "ref-1" };
        _reservations.Setup(r => r.ReserveAsync(It.IsAny<PaymentDetail>(), It.IsAny<PaymentProvider>(), _request, "refund", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRefundReservationResult(new PaymentDetail { ItemId = PaymentId }, refund, "lease", null));
        _initiation.Setup(i => i.SubmitAsync(It.IsAny<PaymentDetail>(), refund, It.IsAny<PaymentProvider>(), "lease", 1000, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRefundOperationResult.Success(new PaymentRefundResponse(), "corr"));

        var result = await CreateAsync();

        result.IsSuccess.Should().BeTrue();
        result.RateLimit.Should().Be(rateLimit);
    }

    [Fact]
    public async Task GetPaymentRefundAsync_ContextFails_ReturnsFailure()
    {
        SetupContextFailure();

        var result = await CreateService().GetPaymentRefundAsync(PaymentId, "ref-1", "corr", CancellationToken.None);

        result.ErrorCode.Should().Be("unauthorized");
    }

    [Fact]
    public async Task GetPaymentRefundAsync_NotFound_ReturnsNotFound()
    {
        _refunds.Setup(r => r.GetPaymentByRefundIdAsync("tenant", "ref-1", It.IsAny<CancellationToken>())).ReturnsAsync((PaymentDetail?)null);

        var result = await CreateService().GetPaymentRefundAsync(PaymentId, "ref-1", "corr", CancellationToken.None);

        result.ErrorCode.Should().Be("payment_refund_not_found");
    }

    [Fact]
    public async Task GetPaymentRefundAsync_Found_ReturnsSuccess()
    {
        var refund = new PaymentRefund { RefundId = "ref-1" };
        var payment = new PaymentDetail { ItemId = PaymentId, Refunds = new List<PaymentRefund> { refund } };
        _refunds.Setup(r => r.GetPaymentByRefundIdAsync("tenant", "ref-1", It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _responses.Setup(m => m.Map(PaymentId, refund)).Returns(new PaymentRefundResponse());

        var result = await CreateService().GetPaymentRefundAsync(PaymentId, "ref-1", "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentRefundsAsync_ContextFails_ReturnsFailure()
    {
        SetupContextFailure();

        var (refunds, failure) = await CreateService().GetPaymentRefundsAsync(PaymentId, "corr", CancellationToken.None);

        refunds.Should().BeNull();
        failure!.ErrorCode.Should().Be("unauthorized");
    }

    [Fact]
    public async Task GetPaymentRefundsAsync_PaymentNull_ReturnsNotFound()
    {
        _refunds.Setup(r => r.GetPaymentAsync("tenant", PaymentId, It.IsAny<CancellationToken>())).ReturnsAsync((PaymentDetail?)null);

        var (refunds, failure) = await CreateService().GetPaymentRefundsAsync(PaymentId, "corr", CancellationToken.None);

        refunds.Should().BeNull();
        failure!.ErrorCode.Should().Be("payment_refund_not_found");
    }

    [Fact]
    public async Task GetPaymentRefundsAsync_Found_ReturnsMappedResponses()
    {
        var payment = new PaymentDetail
        {
            ItemId = PaymentId,
            Refunds = new List<PaymentRefund>
            {
                new() { RefundId = "ref-1", CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1) },
                new() { RefundId = "ref-2", CreatedAtUtc = DateTime.UtcNow }
            }
        };
        _refunds.Setup(r => r.GetPaymentAsync("tenant", PaymentId, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _responses.Setup(m => m.Map(PaymentId, It.IsAny<PaymentRefund>())).Returns(new PaymentRefundResponse());

        var (refunds, failure) = await CreateService().GetPaymentRefundsAsync(PaymentId, "corr", CancellationToken.None);

        failure.Should().BeNull();
        refunds.Should().HaveCount(2);
    }
}
