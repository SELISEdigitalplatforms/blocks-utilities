using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentsControllerExtendedTests
{
    [Fact]
    public async Task CreateRecurringPayment_returns_created_with_location_route()
    {
        var recurring = new Mock<IRecurringPaymentService>();
        recurring.Setup(x => x.CreateRecurringPaymentAsync(
                It.IsAny<CreateRecurringPaymentRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse { PaymentDetailId = "rp-1", PaymentStatus = "PROCESSING" },
                "trace-1", false, 20, 19, 30));
        var controller = Controller(recurring: recurring.Object);

        var result = await controller.CreateRecurringPayment(
            new CreateRecurringPaymentRequest(), "idem", CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(PaymentsController.GetPayment));
        created.RouteValues!["paymentDetailId"].Should().Be("rp-1");
        controller.Response.Headers["RateLimit-Limit"].ToString().Should().Be("20");
    }

    [Fact]
    public async Task CreateRecurringPayment_returns_ok_for_replay()
    {
        var recurring = new Mock<IRecurringPaymentService>();
        recurring.Setup(x => x.CreateRecurringPaymentAsync(
                It.IsAny<CreateRecurringPaymentRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse { PaymentDetailId = "rp-1", PaymentStatus = "PROCESSING" },
                "trace-1", true));
        var controller = Controller(recurring: recurring.Object);

        var result = await controller.CreateRecurringPayment(
            new CreateRecurringPaymentRequest(), null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<PaymentResponse>>().Subject.Meta.Replayed.Should().BeTrue();
    }

    [Fact]
    public async Task CreateRecurringPayment_maps_failure_to_status()
    {
        var recurring = new Mock<IRecurringPaymentService>();
        recurring.Setup(x => x.CreateRecurringPaymentAsync(
                It.IsAny<CreateRecurringPaymentRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Failure(
                PaymentFailureKind.Conflict, "recurring_conflict", "Conflict.", "trace-1"));
        var controller = Controller(recurring: recurring.Object);

        var result = await controller.CreateRecurringPayment(
            new CreateRecurringPaymentRequest(), null, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GetPayment_returns_ok_envelope_on_success()
    {
        var service = new Mock<IPaymentService>();
        service.Setup(x => x.GetPaymentAsync("payment-1", "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse { PaymentDetailId = "payment-1", PaymentStatus = "AUTHORISED" }, "trace-1"));
        var controller = Controller(service.Object);

        var result = await controller.GetPayment("payment-1", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<PaymentResponse>>()
            .Subject.Data!.PaymentDetailId.Should().Be("payment-1");
    }

    [Fact]
    public async Task GetPayment_maps_not_found_failure()
    {
        var service = new Mock<IPaymentService>();
        service.Setup(x => x.GetPaymentAsync("missing", "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Failure(
                PaymentFailureKind.NotFound, "payment_not_found", "Not found.", "trace-1"));
        var controller = Controller(service.Object);

        var result = await controller.GetPayment("missing", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Theory]
    [InlineData(PaymentFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(PaymentFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(PaymentFailureKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(PaymentFailureKind.RateLimited, StatusCodes.Status429TooManyRequests)]
    [InlineData(PaymentFailureKind.ProviderRejected, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(PaymentFailureKind.ProviderFailure, StatusCodes.Status502BadGateway)]
    [InlineData(PaymentFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(PaymentFailureKind.Timeout, StatusCodes.Status504GatewayTimeout)]
    [InlineData(PaymentFailureKind.Unexpected, StatusCodes.Status500InternalServerError)]
    public async Task Create_maps_every_failure_kind_to_its_status_code(
        PaymentFailureKind kind, int expectedStatus)
    {
        var service = new Mock<IPaymentService>();
        service.Setup(x => x.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Failure(kind, "code", "message", "trace-1"));
        var controller = Controller(service.Object);

        var result = await controller.CreatePayment(new MakePaymentRequest(), null, CancellationToken.None);

        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatus);
        objectResult.Value.Should().BeOfType<ApiResponse<PaymentResponse>>()
            .Subject.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreatePaymentRefund_returns_created_with_route_and_rate_headers()
    {
        var refund = new Mock<IPaymentRefundService>();
        refund.Setup(x => x.CreatePaymentRefundAsync(
                "payment-1", It.IsAny<CreatePaymentRefundRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRefundOperationResult.Success(
                new PaymentRefundResponse { RefundId = "refund-1", PaymentDetailId = "payment-1" },
                "trace-1", false,
                new PaymentRateLimitResult { Limit = 5, Remaining = 4, ResetAfterSeconds = 12, RetryAfterSeconds = 3 }));
        var controller = Controller(refund: refund.Object);

        var result = await controller.CreatePaymentRefund(
            "payment-1", new CreatePaymentRefundRequest(), "idem", CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(PaymentsController.GetPaymentRefund));
        created.RouteValues!["refundId"].Should().Be("refund-1");
        created.RouteValues!["paymentDetailId"].Should().Be("payment-1");
        controller.Response.Headers["RateLimit-Limit"].ToString().Should().Be("5");
        controller.Response.Headers["RateLimit-Remaining"].ToString().Should().Be("4");
        controller.Response.Headers["RateLimit-Reset"].ToString().Should().Be("12");
        controller.Response.Headers.RetryAfter.ToString().Should().Be("3");
    }

    [Fact]
    public async Task CreatePaymentRefund_returns_ok_for_replay()
    {
        var refund = new Mock<IPaymentRefundService>();
        refund.Setup(x => x.CreatePaymentRefundAsync(
                "payment-1", It.IsAny<CreatePaymentRefundRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRefundOperationResult.Success(
                new PaymentRefundResponse { RefundId = "refund-1", PaymentDetailId = "payment-1" }, "trace-1", true));
        var controller = Controller(refund: refund.Object);

        var result = await controller.CreatePaymentRefund(
            "payment-1", new CreatePaymentRefundRequest(), null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<ApiResponse<PaymentRefundResponse>>()
            .Subject.Meta.Replayed.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePaymentRefund_maps_failure_without_rate_limit()
    {
        var refund = new Mock<IPaymentRefundService>();
        refund.Setup(x => x.CreatePaymentRefundAsync(
                "payment-1", It.IsAny<CreatePaymentRefundRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRefundOperationResult.Failure(
                PaymentFailureKind.Validation, "refund_invalid", "Invalid.", "trace-1"));
        var controller = Controller(refund: refund.Object);

        var result = await controller.CreatePaymentRefund(
            "payment-1", new CreatePaymentRefundRequest(), null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        controller.Response.Headers.ContainsKey("RateLimit-Limit").Should().BeFalse();
    }

    [Theory]
    [InlineData(PaymentFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(PaymentFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(PaymentFailureKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(PaymentFailureKind.RateLimited, StatusCodes.Status429TooManyRequests)]
    [InlineData(PaymentFailureKind.ProviderRejected, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(PaymentFailureKind.ProviderFailure, StatusCodes.Status502BadGateway)]
    [InlineData(PaymentFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(PaymentFailureKind.Timeout, StatusCodes.Status504GatewayTimeout)]
    [InlineData(PaymentFailureKind.Unexpected, StatusCodes.Status500InternalServerError)]
    public async Task GetPaymentRefund_maps_every_failure_kind(
        PaymentFailureKind kind, int expectedStatus)
    {
        var refund = new Mock<IPaymentRefundService>();
        refund.Setup(x => x.GetPaymentRefundAsync(
                "payment-1", "refund-1", "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRefundOperationResult.Failure(kind, "code", "message", "trace-1"));
        var controller = Controller(refund: refund.Object);

        var result = await controller.GetPaymentRefund("payment-1", "refund-1", CancellationToken.None);

        result.Should().BeAssignableTo<ObjectResult>().Subject.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task GetPaymentRefund_returns_ok_on_success()
    {
        var refund = new Mock<IPaymentRefundService>();
        refund.Setup(x => x.GetPaymentRefundAsync(
                "payment-1", "refund-1", "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRefundOperationResult.Success(
                new PaymentRefundResponse { RefundId = "refund-1", PaymentDetailId = "payment-1" }, "trace-1"));
        var controller = Controller(refund: refund.Object);

        var result = await controller.GetPaymentRefund("payment-1", "refund-1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<ApiResponse<PaymentRefundResponse>>()
            .Subject.Data!.RefundId.Should().Be("refund-1");
    }

    [Fact]
    public async Task GetPaymentRefunds_returns_ok_list_on_success()
    {
        var refund = new Mock<IPaymentRefundService>();
        var list = new List<PaymentRefundResponse>
        {
            new() { RefundId = "refund-1", PaymentDetailId = "payment-1" },
            new() { RefundId = "refund-2", PaymentDetailId = "payment-1" }
        };
        refund.Setup(x => x.GetPaymentRefundsAsync("payment-1", "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((list, null));
        var controller = Controller(refund: refund.Object);

        var result = await controller.GetPaymentRefunds("payment-1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<ApiResponse<IReadOnlyList<PaymentRefundResponse>>>()
            .Subject.Data!.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPaymentRefunds_maps_failure()
    {
        var refund = new Mock<IPaymentRefundService>();
        refund.Setup(x => x.GetPaymentRefundsAsync("payment-1", "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, PaymentRefundOperationResult.Failure(
                PaymentFailureKind.NotFound, "payment_not_found", "Not found.", "trace-1")));
        var controller = Controller(refund: refund.Object);

        var result = await controller.GetPaymentRefunds("payment-1", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreatePaymentCapture_returns_created_with_route_and_rate_headers()
    {
        var capture = new Mock<IPaymentCaptureService>();
        capture.Setup(x => x.CreatePaymentCaptureAsync(
                "payment-1", It.IsAny<CreatePaymentCaptureRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentCaptureOperationResult.Success(
                new PaymentCaptureResponse { CaptureId = "capture-1", PaymentDetailId = "payment-1" },
                "trace-1", false,
                new PaymentRateLimitResult { Limit = 7, Remaining = 6, ResetAfterSeconds = 15, RetryAfterSeconds = 0 }));
        var controller = Controller(capture: capture.Object);

        var result = await controller.CreatePaymentCapture(
            "payment-1", new CreatePaymentCaptureRequest(), "idem", CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(PaymentsController.GetPaymentCapture));
        created.RouteValues!["captureId"].Should().Be("capture-1");
        controller.Response.Headers["RateLimit-Limit"].ToString().Should().Be("7");
        controller.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task CreatePaymentCapture_returns_ok_for_replay()
    {
        var capture = new Mock<IPaymentCaptureService>();
        capture.Setup(x => x.CreatePaymentCaptureAsync(
                "payment-1", It.IsAny<CreatePaymentCaptureRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentCaptureOperationResult.Success(
                new PaymentCaptureResponse { CaptureId = "capture-1", PaymentDetailId = "payment-1" }, "trace-1", true));
        var controller = Controller(capture: capture.Object);

        var result = await controller.CreatePaymentCapture(
            "payment-1", new CreatePaymentCaptureRequest(), null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<ApiResponse<PaymentCaptureResponse>>()
            .Subject.Meta.Replayed.Should().BeTrue();
    }

    [Theory]
    [InlineData(PaymentFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(PaymentFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(PaymentFailureKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(PaymentFailureKind.RateLimited, StatusCodes.Status429TooManyRequests)]
    [InlineData(PaymentFailureKind.ProviderRejected, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(PaymentFailureKind.ProviderFailure, StatusCodes.Status502BadGateway)]
    [InlineData(PaymentFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(PaymentFailureKind.Timeout, StatusCodes.Status504GatewayTimeout)]
    [InlineData(PaymentFailureKind.Unexpected, StatusCodes.Status500InternalServerError)]
    public async Task GetPaymentCapture_maps_every_failure_kind(
        PaymentFailureKind kind, int expectedStatus)
    {
        var capture = new Mock<IPaymentCaptureService>();
        capture.Setup(x => x.GetPaymentCaptureAsync(
                "payment-1", "capture-1", "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentCaptureOperationResult.Failure(kind, "code", "message", "trace-1"));
        var controller = Controller(capture: capture.Object);

        var result = await controller.GetPaymentCapture("payment-1", "capture-1", CancellationToken.None);

        result.Should().BeAssignableTo<ObjectResult>().Subject.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task GetPaymentCapture_returns_ok_on_success()
    {
        var capture = new Mock<IPaymentCaptureService>();
        capture.Setup(x => x.GetPaymentCaptureAsync(
                "payment-1", "capture-1", "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentCaptureOperationResult.Success(
                new PaymentCaptureResponse { CaptureId = "capture-1", PaymentDetailId = "payment-1" }, "trace-1"));
        var controller = Controller(capture: capture.Object);

        var result = await controller.GetPaymentCapture("payment-1", "capture-1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<ApiResponse<PaymentCaptureResponse>>()
            .Subject.Data!.CaptureId.Should().Be("capture-1");
    }

    private static PaymentsController Controller(
        IPaymentService? service = null,
        IRecurringPaymentService? recurring = null,
        IPaymentRefundService? refund = null,
        IPaymentCaptureService? capture = null,
        IPaymentQueryService? query=null )
    {
        var controller = new PaymentsController(
            service ?? Mock.Of<IPaymentService>(),
            recurring ?? Mock.Of<IRecurringPaymentService>(),
            refund ?? Mock.Of<IPaymentRefundService>(),
            capture ?? Mock.Of<IPaymentCaptureService>(),
            query ?? Mock.Of<IPaymentQueryService>(),
            Mock.Of<IPaymentProviderRegistrationService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.TraceIdentifier = "trace-1";
        return controller;
    }
}
