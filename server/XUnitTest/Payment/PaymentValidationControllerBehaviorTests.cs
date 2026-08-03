using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentValidationControllerBehaviorTests
{
    [Fact]
    public async Task ValidatePayment_redirects_with_location_and_retry_after_headers()
    {
        var service = new Mock<ICheckoutCallbackService>();
        service.Setup(x => x.ProcessAsync(
                It.IsAny<CheckoutCallbackRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutCallbackResult.Redirect("https://shop.example/return")
                with { RetryAfterSeconds = 4 });
        var controller = Controller(service.Object);

        var result = await controller.ValidatePayment("state", "session", "result", CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>()
            .Subject.StatusCode.Should().Be(StatusCodes.Status303SeeOther);
        controller.Response.Headers.Location.ToString().Should().Be("https://shop.example/return");
        controller.Response.Headers.RetryAfter.ToString().Should().Be("4");
    }

    [Theory]
    [InlineData(PaymentFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(PaymentFailureKind.RateLimited, StatusCodes.Status429TooManyRequests)]
    [InlineData(PaymentFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(PaymentFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(PaymentFailureKind.ProviderFailure, StatusCodes.Status400BadRequest)]
    public async Task ValidatePayment_maps_failure_kind_to_status_code(
        PaymentFailureKind kind, int expectedStatus)
    {
        var service = new Mock<ICheckoutCallbackService>();
        service.Setup(x => x.ProcessAsync(
                It.IsAny<CheckoutCallbackRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutCallbackResult.Failure(kind, "callback_error", "Callback failed."));
        var controller = Controller(service.Object);

        var result = await controller.ValidatePayment(null, null, null, CancellationToken.None);

        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatus);
        objectResult.Value.Should().BeOfType<ApiResponse<object>>()
            .Subject.Error!.Code.Should().Be("callback_error");
    }

    [Fact]
    public async Task ValidatePayment_forwards_client_address_from_connection()
    {
        var service = new Mock<ICheckoutCallbackService>();
        service.Setup(x => x.ProcessAsync(
                It.IsAny<CheckoutCallbackRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutCallbackResult.Failure(
                PaymentFailureKind.Validation, "invalid", "Invalid."));
        var controller = Controller(service.Object);
        controller.HttpContext.Connection.RemoteIpAddress =
            System.Net.IPAddress.Parse("203.0.113.7");

        await controller.ValidatePayment("s", "sid", "res", CancellationToken.None);

        service.Verify(x => x.ProcessAsync(
            It.Is<CheckoutCallbackRequest>(r =>
                r.State == "s" && r.SessionId == "sid" && r.SessionResult == "res"),
            "203.0.113.7",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidatePayment_uses_unknown_client_address_when_connection_has_no_ip()
    {
        var service = new Mock<ICheckoutCallbackService>();
        service.Setup(x => x.ProcessAsync(
                It.IsAny<CheckoutCallbackRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutCallbackResult.Failure(
                PaymentFailureKind.Validation, "invalid", "Invalid."));
        var controller = Controller(service.Object);

        await controller.ValidatePayment(null, null, null, CancellationToken.None);

        service.Verify(x => x.ProcessAsync(
            It.IsAny<CheckoutCallbackRequest>(), "unknown", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static PaymentValidationController Controller(ICheckoutCallbackService service)
    {
        var controller = new PaymentValidationController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.TraceIdentifier = "trace-1";
        return controller;
    }
}
