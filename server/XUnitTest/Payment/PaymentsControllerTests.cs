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

public sealed class PaymentsControllerTests
{
    [Fact]
    public async Task Get_payments_returns_query_envelope_and_rate_headers()
    {
        var queryService = new Mock<IPaymentQueryService>();
        queryService.Setup(x => x.GetPaymentsAsync(
                It.IsAny<GetPaymentsRequest>(),
                "trace-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentQueryOperationResult.Success(
                new PaymentListResponse
                {
                    Items =
                    [
                        new PaymentListItemResponse
                        {
                            PaymentDetailId = "payment-1",
                            ProviderName = "ADYEN-ONLINE",
                            Amount = 10,
                            CurrencyCode = "CHF",
                            PaymentDateUtc = DateTime.UtcNow,
                            PaymentStatus = "AUTHORIZED"
                        }
                    ]
                },
                "trace-1",
                new PaymentRateLimitResult
                {
                    IsAllowed = true,
                    Limit = 120,
                    Remaining = 119,
                    ResetAfterSeconds = 1
                }));
        var controller = Controller(
            Mock.Of<IPaymentService>(),
            queryService.Object);

        var result = await controller.GetPayments(
            new GetPaymentsRequest(),
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value
            .Should()
            .BeOfType<ApiResponse<PaymentListResponse>>()
            .Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data!.Items.Should().ContainSingle();
        controller.Response.Headers["RateLimit-Limit"]
            .ToString()
            .Should()
            .Be("120");
    }

    [Fact]
    public async Task Create_returns_created_envelope_location_route_and_rate_headers()
    {
        var service = new Mock<IPaymentService>();
        service.Setup(x => x.MakePaymentAsync(It.IsAny<MakePaymentRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse { PaymentDetailId = "payment-1", PaymentStatus = "PROCESSING" },
                "trace-1", false, 10, 9, 6));
        var controller = Controller(service.Object);

        var result = await controller.CreatePayment(new MakePaymentRequest(), Guid.NewGuid().ToString(), CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(PaymentsController.GetPayment));
        created.RouteValues!["paymentDetailId"].Should().Be("payment-1");
        var envelope = created.Value.Should().BeOfType<ApiResponse<PaymentResponse>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Meta.CorrelationId.Should().Be("trace-1");
        controller.Response.Headers["RateLimit-Limit"].ToString().Should().Be("10");
        controller.Response.Headers["RateLimit-Remaining"].ToString().Should().Be("9");
        controller.Response.Headers["RateLimit-Reset"].ToString().Should().Be("6");
    }

    [Fact]
    public async Task Create_returns_ok_and_replay_metadata_for_completed_replay()
    {
        var service = new Mock<IPaymentService>();
        service.Setup(x => x.MakePaymentAsync(It.IsAny<MakePaymentRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse { PaymentDetailId = "payment-1", PaymentStatus = "PROCESSING" },
                "trace-1", true));
        var controller = Controller(service.Object);

        var result = await controller.CreatePayment(new MakePaymentRequest(), Guid.NewGuid().ToString(), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeOfType<ApiResponse<PaymentResponse>>().Subject.Meta.Replayed.Should().BeTrue();
    }

    [Fact]
    public async Task Create_maps_rate_limit_to_429_with_retry_after_and_safe_error_envelope()
    {
        var service = new Mock<IPaymentService>();
        service.Setup(x => x.MakePaymentAsync(It.IsAny<MakePaymentRequest>(), It.IsAny<string>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Failure(
                PaymentFailureKind.RateLimited,
                "payment_rate_limit_exceeded",
                "Too many payment requests.",
                "trace-1",
                retryAfterSeconds: 2,
                limit: 10,
                remaining: 0,
                resetAfterSeconds: 6));
        var controller = Controller(service.Object);

        var result = await controller.CreatePayment(new MakePaymentRequest(), Guid.NewGuid().ToString(), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        var envelope = objectResult.Value.Should().BeOfType<ApiResponse<PaymentResponse>>().Subject;
        envelope.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("payment_rate_limit_exceeded");
        controller.Response.Headers.RetryAfter.ToString().Should().Be("2");
    }

    private static PaymentsController Controller(
        IPaymentService service,
        IPaymentQueryService? queryService = null)
    {
        var controller = new PaymentsController(
            service,
            Mock.Of<IRecurringPaymentService>(),
            Mock.Of<IPaymentRefundService>(),
            Mock.Of<IPaymentCaptureService>(),
            queryService ?? Mock.Of<IPaymentQueryService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.TraceIdentifier = "trace-1";
        return controller;
    }
}
