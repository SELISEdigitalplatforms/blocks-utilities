using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Refunds and captures apply the same organization scope as reading the payment.
/// </summary>
/// <remarks>
/// Guards against one organization moving another organization's money: anyone in the tenant
/// holding a payment identifier could refund it or capture it, and read its refund and capture
/// history, because only the tenant was checked.
/// </remarks>
public sealed class PaymentRefundCaptureOrganizationScopeTests
{
    private const string PaymentId = "7f1c2a4e-9d3b-4c1a-8e2f-5b6a7c8d9e01";
    private const string IdempotencyKey = "0b9d1e2f-3a4b-4c5d-8e6f-7a8b9c0d1e2f";

    [Theory]
    [InlineData("organization-1", "organization-1", true)]
    [InlineData("organization-1", "organization-2", false)]
    [InlineData("organization-1", null, true)]
    [InlineData(null, "organization-2", true)]
    [InlineData("console", "organization-2", true)]
    public void The_scope_matches_the_payment_listing(
        string? callerOrganization,
        string? paymentOrganization,
        bool expected)
    {
        PaymentOrganizationScope.MayAccess(
                Payment(paymentOrganization),
                callerOrganization,
                new PaymentOptions { ConsoleOrganizationId = "console" })
            .Should().Be(expected,
                "an organization reaches its own and pre-organization payments, a tenant-wide " +
                "caller and the console reach every organization");
    }

    /// <summary>
    /// Reported as not found, like reading the payment, so the response cannot confirm that the
    /// identifier exists in another organization.
    /// </summary>
    [Fact]
    public async Task Another_organizations_payment_cannot_be_refunded()
    {
        var result = await RefundPreflightAsync("organization-2");

        result.IsSuccess.Should().BeFalse();
        result.Failure!.FailureKind.Should().Be(PaymentFailureKind.NotFound,
            "a refund would return another organization's money");
        result.Failure.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task An_organizations_own_payment_passes_the_scope_check_for_a_refund()
    {
        var result = await RefundPreflightAsync("organization-1");

        result.Failure!.FailureKind.Should().NotBe(PaymentFailureKind.NotFound,
            "the payment is the caller's own and goes on to the refundability checks");
    }

    [Fact]
    public async Task Another_organizations_payment_cannot_be_captured()
    {
        var result = await CapturePreflightAsync("organization-2");

        result.IsSuccess.Should().BeFalse();
        result.Failure!.FailureKind.Should().Be(PaymentFailureKind.NotFound,
            "a capture would take another organization's authorized funds");
        result.Failure.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task An_organizations_own_payment_passes_the_scope_check_for_a_capture()
    {
        var result = await CapturePreflightAsync("organization-1");

        result.Failure!.FailureKind.Should().NotBe(PaymentFailureKind.NotFound,
            "the payment is the caller's own and goes on to the capturability checks");
    }

    [Theory]
    [InlineData("organization-1", true)]
    [InlineData("organization-2", false)]
    public async Task Refund_history_is_scoped_to_the_organization(
        string paymentOrganization,
        bool visible)
    {
        var payment = Payment(paymentOrganization);
        payment.Refunds.Add(new PaymentRefund { RefundId = "refund-1" });
        var refunds = new Mock<IPaymentRefundRepository>();
        refunds.Setup(item => item.GetPaymentAsync("tenant-1", PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        refunds.Setup(item => item.GetPaymentByRefundIdAsync("tenant-1", "refund-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        var service = new PaymentRefundService(
            ContextResolver(),
            Mock.Of<IPaymentRefundPreflightService>(),
            Mock.Of<IPaymentDistributedLock>(),
            Mock.Of<IPaymentRefundReservationService>(),
            Mock.Of<IPaymentRefundInitiationService>(),
            refunds.Object,
            Mock.Of<IPaymentRefundResponseMapper>(),
            Options());

        var single = await service.GetPaymentRefundAsync(PaymentId, "refund-1", "corr-1", CancellationToken.None);
        var (_, listFailure) = await service.GetPaymentRefundsAsync(PaymentId, "corr-1", CancellationToken.None);

        single.IsSuccess.Should().Be(visible, "a refund names the amount and reason of another organization's payment");
        (listFailure == null).Should().Be(visible);
    }

    [Theory]
    [InlineData("organization-1", true)]
    [InlineData("organization-2", false)]
    public async Task A_capture_is_scoped_to_the_organization(
        string paymentOrganization,
        bool visible)
    {
        var payment = Payment(paymentOrganization);
        payment.Captures.Add(new PaymentCapture { CaptureId = "capture-1" });
        var captures = new Mock<IPaymentCaptureRepository>();
        captures.Setup(item => item.GetPaymentByCaptureIdAsync("tenant-1", "capture-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        var service = new PaymentCaptureService(
            ContextResolver(),
            Mock.Of<IPaymentCapturePreflightService>(),
            Mock.Of<IPaymentDistributedLock>(),
            Mock.Of<IPaymentCaptureReservationService>(),
            Mock.Of<IPaymentCaptureInitiationService>(),
            captures.Object,
            Mock.Of<IPaymentCaptureResponseMapper>(),
            Options());

        var result = await service.GetPaymentCaptureAsync(PaymentId, "capture-1", "corr-1", CancellationToken.None);

        result.IsSuccess.Should().Be(visible);
    }

    private static Task<PaymentRefundPreflightResult> RefundPreflightAsync(string paymentOrganization)
    {
        var refunds = new Mock<IPaymentRefundRepository>();
        refunds.Setup(item => item.GetPaymentAsync("tenant-1", PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Payment(paymentOrganization));
        var preflight = new PaymentRefundPreflightService(
            ValidValidator<CreatePaymentRefundRequest>(),
            Mock.Of<ICurrencyMinorUnitResolver>(),
            Mock.Of<IPaymentRateLimiter>(),
            refunds.Object,
            Mock.Of<IPaymentRepository>(),
            Mock.Of<IPaymentProviderCache>(),
            Mock.Of<IPaymentFundReturnStrategyResolver>(),
            Options());

        return preflight.ExecuteAsync(
            PaymentId, new CreatePaymentRefundRequest(), IdempotencyKey, Context(), "corr-1", CancellationToken.None);
    }

    private static Task<PaymentCapturePreflightResult> CapturePreflightAsync(string paymentOrganization)
    {
        var captures = new Mock<IPaymentCaptureRepository>();
        captures.Setup(item => item.GetPaymentAsync("tenant-1", PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Payment(paymentOrganization));
        var preflight = new PaymentCapturePreflightService(
            ValidValidator<CreatePaymentCaptureRequest>(),
            Mock.Of<ICurrencyMinorUnitResolver>(),
            Mock.Of<IPaymentRateLimiter>(),
            captures.Object,
            Mock.Of<IPaymentRepository>(),
            Mock.Of<IPaymentProviderCache>(),
            Options());

        return preflight.ExecuteAsync(
            PaymentId, new CreatePaymentCaptureRequest(), IdempotencyKey, Context(), "corr-1", CancellationToken.None);
    }

    /// <summary>
    /// A payment in no refundable or capturable state, so a caller who passes the scope check
    /// stops at the next one without the test needing a provider.
    /// </summary>
    private static PaymentDetail Payment(string? organizationId) => new()
    {
        ItemId = PaymentId,
        TenantId = "tenant-1",
        OrganizationId = organizationId,
        PaymentStatus = PaymentStatuses.Initiating
    };

    private static PaymentExecutionContext Context() => new("tenant-1", "actor-1", "organization-1");

    private static IPaymentExecutionContextResolver ContextResolver()
    {
        var resolver = new Mock<IPaymentExecutionContextResolver>();
        resolver.Setup(item => item.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(Context(), null));
        return resolver.Object;
    }

    private static IOptionsMonitor<PaymentOptions> Options() =>
        Mock.Of<IOptionsMonitor<PaymentOptions>>(monitor =>
            monitor.CurrentValue == new PaymentOptions { ConsoleOrganizationId = "console" });

    private static IValidator<T> ValidValidator<T>()
    {
        var validator = new Mock<IValidator<T>>();
        validator.Setup(item => item.ValidateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        return validator.Object;
    }
}
