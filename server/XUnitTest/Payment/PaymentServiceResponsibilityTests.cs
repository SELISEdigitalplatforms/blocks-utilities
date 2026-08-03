using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentServiceResponsibilityTests
{
    [Fact]
    public async Task Make_payment_stops_when_authenticated_context_cannot_be_resolved()
    {
        var fixture = new Fixture();
        var failure = PaymentOperationResult.Failure(
            PaymentFailureKind.Unavailable,
            "payment_context_missing",
            "Authenticated tenant context is unavailable.",
            "trace-1");
        fixture.ContextResolver
            .Setup(x => x.Resolve("trace-1"))
            .Returns(new PaymentContextResolution(null, failure));

        var result = await fixture.Service.MakePaymentAsync(
            new MakePaymentRequest(),
            Guid.NewGuid().ToString(),
            "trace-1",
            CancellationToken.None);

        result.Should().BeSameAs(failure);
        fixture.Preflight.VerifyNoOtherCalls();
        fixture.Reservation.VerifyNoOtherCalls();
        fixture.Initiation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Make_payment_stops_at_preflight_failure_without_locking_or_persisting()
    {
        var fixture = new Fixture();
        var context = fixture.ArrangeContext();
        var failure = PaymentOperationResult.Failure(
            PaymentFailureKind.Validation,
            "payment_validation_failed",
            "The payment request is invalid.",
            "trace-1");
        fixture.Preflight
            .Setup(x => x.ExecuteAsync(
                It.IsAny<MakePaymentRequest>(),
                It.IsAny<string>(),
                context,
                "trace-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentPreflightResult(0, null, failure));

        var result = await fixture.Service.MakePaymentAsync(
            new MakePaymentRequest(),
            Guid.NewGuid().ToString(),
            "trace-1",
            CancellationToken.None);

        result.Should().BeSameAs(failure);
        fixture.DistributedLock.VerifyNoOtherCalls();
        fixture.Reservation.VerifyNoOtherCalls();
        fixture.Initiation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Make_payment_orchestrates_each_responsibility_once_and_releases_the_lock()
    {
        var fixture = new Fixture();
        var context = fixture.ArrangeContext();
        var request = new MakePaymentRequest
        {
            ProviderName = "ADYEN-ONLINE",
            Amount = 10,
            CurrencyCode = "USD",
            OrderId = "order-1"
        };
        var rateLimit = new PaymentRateLimitResult
        {
            IsAllowed = true,
            Limit = 10,
            Remaining = 9,
            ResetAfterSeconds = 6
        };
        fixture.Preflight
            .Setup(x => x.ExecuteAsync(request, It.IsAny<string>(), context, "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentPreflightResult(1000, rateLimit, null));
        var lockHandle = new TrackingLockHandle();
        fixture.DistributedLock
            .Setup(x => x.TryAcquireAsync(It.Is<string>(value => value.Length == 32), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockHandle);
        var payment = new PaymentDetail { ItemId = "payment-1", TenantId = context.TenantId };
        fixture.Reservation
            .Setup(x => x.ReserveAsync(request, context, It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentReservationResult(payment, "lease-1", null));
        var initiated = PaymentOperationResult.Success(
            new PaymentResponse { PaymentDetailId = payment.ItemId, PaymentStatus = "PROCESSING" },
            "trace-1");
        fixture.Initiation
            .Setup(x => x.InitiateAsync(request, context, payment, "lease-1", 1000, "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(initiated);

        var result = await fixture.Service.MakePaymentAsync(
            request,
            Guid.NewGuid().ToString(),
            "trace-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.RateLimit.Should().Be(10);
        result.RateLimitRemaining.Should().Be(9);
        result.RateLimitResetSeconds.Should().Be(6);
        lockHandle.IsDisposed.Should().BeTrue();
        fixture.ContextResolver.Verify(x => x.Resolve("trace-1"), Times.Once);
        fixture.Preflight.VerifyAll();
        fixture.DistributedLock.VerifyAll();
        fixture.Reservation.VerifyAll();
        fixture.Initiation.VerifyAll();
    }

    [Fact]
    public async Task Make_payment_returns_terminal_reservation_result_with_rate_limit()
    {
        var fixture = new Fixture();
        var context = fixture.ArrangeContext();
        var request = new MakePaymentRequest
        {
            ProviderName = "ADYEN-ONLINE",
            Amount = 10,
            CurrencyCode = "USD",
            OrderId = "order-1"
        };
        var rateLimit = new PaymentRateLimitResult
        {
            IsAllowed = true,
            Limit = 10,
            Remaining = 9
        };
        fixture.Preflight
            .Setup(x => x.ExecuteAsync(request, It.IsAny<string>(), context, "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentPreflightResult(1000, rateLimit, null));
        fixture.DistributedLock
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPaymentLockHandle?)null);
        var replay = PaymentOperationResult.Success(
            new PaymentResponse { PaymentDetailId = "existing-1" }, "trace-1", replay: true);
        fixture.Reservation
            .Setup(x => x.ReserveAsync(request, context, It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentReservationResult(null, null, replay));

        var result = await fixture.Service.MakePaymentAsync(
            request, Guid.NewGuid().ToString(), "trace-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.RateLimit.Should().Be(10);
        fixture.Initiation.Verify(x => x.InitiateAsync(
            It.IsAny<MakePaymentRequest>(), It.IsAny<PaymentExecutionContext>(),
            It.IsAny<PaymentDetail>(), It.IsAny<string>(), It.IsAny<long>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_payment_returns_failure_when_context_cannot_be_resolved()
    {
        var fixture = new Fixture();
        fixture.ContextResolver
            .Setup(x => x.Resolve("trace-1"))
            .Returns(new PaymentContextResolution(
                null,
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Unavailable, "payment_context_missing",
                    "missing", "trace-1")));

        var result = await fixture.Service.GetPaymentAsync(
            "payment-1", "trace-1", CancellationToken.None);

        result.ErrorCode.Should().Be("payment_context_missing");
    }

    [Fact]
    public async Task Get_payment_returns_not_found_when_repository_has_no_payment()
    {
        var fixture = new Fixture();
        var context = fixture.ArrangeContext();
        fixture.Repository
            .Setup(x => x.GetByIdAsync(context.TenantId, "payment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        var result = await fixture.Service.GetPaymentAsync(
            "payment-1", "trace-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task Get_payment_maps_found_payment_to_success()
    {
        var fixture = new Fixture();
        var context = fixture.ArrangeContext();
        var payment = new PaymentDetail { ItemId = "payment-1", TenantId = context.TenantId };
        fixture.Repository
            .Setup(x => x.GetByIdAsync(context.TenantId, "payment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        var mapped = new PaymentResponse { PaymentDetailId = "payment-1" };
        fixture.ResponseMapper.Setup(x => x.Map(payment)).Returns(mapped);

        var result = await fixture.Service.GetPaymentAsync(
            "payment-1", "trace-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Payment.Should().BeSameAs(mapped);
    }

    [Fact]
    public async Task Recover_routes_recurring_charge_to_recurring_initiation()
    {
        var fixture = new Fixture();
        var payment = new PaymentDetail
        {
            ItemId = "payment-1",
            PaymentFlow = "RECURRING_CHARGE"
        };
        fixture.RecurringInitiation
            .Setup(x => x.RecoverAsync(payment, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await fixture.Service.RecoverAsync(payment, CancellationToken.None);

        fixture.RecurringInitiation.Verify(
            x => x.RecoverAsync(payment, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Initiation.Verify(
            x => x.RecoverAsync(It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Recover_routes_standard_payment_to_initiation_service()
    {
        var fixture = new Fixture();
        var payment = new PaymentDetail
        {
            ItemId = "payment-1",
            PaymentFlow = "STANDARD"
        };
        fixture.Initiation
            .Setup(x => x.RecoverAsync(payment, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await fixture.Service.RecoverAsync(payment, CancellationToken.None);

        fixture.Initiation.Verify(
            x => x.RecoverAsync(payment, It.IsAny<CancellationToken>()), Times.Once);
        fixture.RecurringInitiation.Verify(
            x => x.RecoverAsync(It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class Fixture
    {
        public Mock<IPaymentExecutionContextResolver> ContextResolver { get; } = new();
        public Mock<IPaymentPreflightService> Preflight { get; } = new();
        public Mock<IPaymentDistributedLock> DistributedLock { get; } = new();
        public Mock<IPaymentReservationService> Reservation { get; } = new();
        public Mock<IPaymentInitiationService> Initiation { get; } = new();
        public Mock<IPaymentRepository> Repository { get; } = new();
        public Mock<IPaymentResponseMapper> ResponseMapper { get; } = new();
        public Mock<IRecurringPaymentInitiationService>
            RecurringInitiation { get; } = new();

        public PaymentService Service => new(
            ContextResolver.Object,
            Preflight.Object,
            DistributedLock.Object,
            Reservation.Object,
            Initiation.Object,
            Repository.Object,
            ResponseMapper.Object,
            RecurringInitiation.Object);

        public PaymentExecutionContext ArrangeContext()
        {
            var context = new PaymentExecutionContext("tenant-1", "actor-1", "organization-1");
            ContextResolver
                .Setup(x => x.Resolve("trace-1"))
                .Returns(new PaymentContextResolution(context, null));
            return context;
        }
    }

    private sealed class TrackingLockHandle : IPaymentLockHandle
    {
        public string Token { get; } = Guid.NewGuid().ToString("N");
        public bool IsDisposed { get; private set; }

        public Task<bool> RenewAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
