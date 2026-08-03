using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class RecurringPaymentServiceTests
{
    private const string TenantId = "tenant-1";
    private const string CorrelationId = "correlation-1";
    private const string IdempotencyKey = "idempotency-1";

    [Fact]
    public async Task Context_resolution_failure_is_returned_directly()
    {
        var fixture = new Fixture();
        var failure = PaymentOperationResult.Failure(
            PaymentFailureKind.Unexpected,
            "unauthorized",
            "no context",
            CorrelationId);
        fixture.ContextResolver
            .Setup(resolver => resolver.Resolve(CorrelationId))
            .Returns(new PaymentContextResolution(null, failure));

        var result = await fixture.Service.CreateRecurringPaymentAsync(
            fixture.Request, IdempotencyKey, CorrelationId, CancellationToken.None);

        result.Should().BeSameAs(failure);
        fixture.Preflight.Verify(preflight => preflight.ExecuteAsync(
            It.IsAny<CreateRecurringPaymentRequest>(),
            It.IsAny<string>(),
            It.IsAny<PaymentExecutionContext>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Preflight_failure_without_rate_limit_is_returned_directly()
    {
        var fixture = new Fixture();
        fixture.ArrangeContext();
        var failure = PaymentOperationResult.Failure(
            PaymentFailureKind.Validation, "invalid", "bad request", CorrelationId);
        fixture.ArrangePreflight(new RecurringPaymentPreflightResult(
            0, null, null, null, null, failure));

        var result = await fixture.Service.CreateRecurringPaymentAsync(
            fixture.Request, IdempotencyKey, CorrelationId, CancellationToken.None);

        result.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task Preflight_failure_with_rate_limit_carries_rate_limit_headers()
    {
        var fixture = new Fixture();
        fixture.ArrangeContext();
        var failure = PaymentOperationResult.Failure(
            PaymentFailureKind.RateLimited, "rate_limited", "slow down", CorrelationId);
        fixture.ArrangePreflight(new RecurringPaymentPreflightResult(
            0, fixture.RateLimit, null, null, null, failure));

        var result = await fixture.Service.CreateRecurringPaymentAsync(
            fixture.Request, IdempotencyKey, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("rate_limited");
        result.RateLimit.Should().Be(fixture.RateLimit.Limit);
        result.RateLimitRemaining.Should().Be(fixture.RateLimit.Remaining);
    }

    [Fact]
    public async Task Reservation_that_cannot_initiate_returns_terminal_result_with_rate_limit()
    {
        var fixture = new Fixture();
        fixture.ArrangeContext();
        fixture.ArrangeSuccessfulPreflight();
        var terminal = PaymentOperationResult.Failure(
            PaymentFailureKind.Conflict, "duplicate", "already processed", CorrelationId);
        fixture.ArrangeReservation(new PaymentReservationResult(null, null, terminal));

        var result = await fixture.Service.CreateRecurringPaymentAsync(
            fixture.Request, IdempotencyKey, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("duplicate");
        result.RateLimit.Should().Be(fixture.RateLimit.Limit);
        fixture.Initiation.Verify(initiation => initiation.InitiateAsync(
            It.IsAny<PaymentDetail>(),
            It.IsAny<StoredPaymentMethod>(),
            It.IsAny<PaymentProvider>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Successful_flow_initiates_payment_and_applies_rate_limit()
    {
        var fixture = new Fixture();
        fixture.ArrangeContext();
        fixture.ArrangeSuccessfulPreflight();
        var payment = new PaymentDetail { ItemId = "payment-1", TenantId = TenantId };
        fixture.ArrangeReservation(new PaymentReservationResult(payment, "lease-1", null));
        var success = PaymentOperationResult.Success(
            new PaymentResponse { PaymentDetailId = "payment-1" }, CorrelationId);
        fixture.Initiation
            .Setup(initiation => initiation.InitiateAsync(
                payment,
                fixture.StoredMethod,
                fixture.Provider,
                "lease-1",
                fixture.MinorUnits,
                CorrelationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(success);

        var result = await fixture.Service.CreateRecurringPaymentAsync(
            fixture.Request, IdempotencyKey, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.RateLimit.Should().Be(fixture.RateLimit.Limit);
        result.RateLimitRemaining.Should().Be(fixture.RateLimit.Remaining);
        fixture.Initiation.VerifyAll();
    }

    private sealed class Fixture
    {
        public Mock<IPaymentExecutionContextResolver> ContextResolver { get; } = new();
        public Mock<IRecurringPaymentPreflightService> Preflight { get; } = new();
        public Mock<IPaymentDistributedLock> DistributedLock { get; } = new();
        public Mock<IRecurringPaymentReservationService> Reservations { get; } = new();
        public Mock<IRecurringPaymentInitiationService> Initiation { get; } = new();

        public CreateRecurringPaymentRequest Request { get; } = new()
        {
            StoredPaymentMethodId = "method-1",
            Amount = 10m,
            CurrencyCode = "USD",
            OrderId = "order-1"
        };

        public PaymentRateLimitResult RateLimit { get; } = new()
        {
            IsAllowed = true,
            Limit = 100,
            Remaining = 42,
            ResetAfterSeconds = 30
        };

        public PaymentProvider Provider { get; } = new();
        public StoredPaymentMethod StoredMethod { get; } = new();
        public long MinorUnits => 1000;

        public Fixture()
        {
            DistributedLock
                .Setup(locks => locks.TryAcquireAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<IPaymentLockHandle>());
        }

        public PaymentExecutionContext Context { get; } =
            new(TenantId, "actor-1", null);

        public RecurringPaymentService Service => new(
            ContextResolver.Object,
            Preflight.Object,
            DistributedLock.Object,
            Reservations.Object,
            Initiation.Object);

        public void ArrangeContext() =>
            ContextResolver
                .Setup(resolver => resolver.Resolve(CorrelationId))
                .Returns(new PaymentContextResolution(Context, null));

        public void ArrangePreflight(RecurringPaymentPreflightResult result) =>
            Preflight
                .Setup(preflight => preflight.ExecuteAsync(
                    Request, IdempotencyKey, Context, CorrelationId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);

        public void ArrangeSuccessfulPreflight() =>
            ArrangePreflight(new RecurringPaymentPreflightResult(
                MinorUnits, RateLimit, Provider, StoredMethod, "shopper-1", null));

        public void ArrangeReservation(PaymentReservationResult result) =>
            Reservations
                .Setup(reservations => reservations.ReserveAsync(
                    Request, Context, "shopper-1", IdempotencyKey, CorrelationId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
    }
}
