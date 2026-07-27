using Api.Controllers;
using Api.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentWebhooksControllerBehaviorTests
{
    private const string EmptyNotifications = "{\"notificationItems\":[]}";

    [Theory]
    [InlineData(WebhookRequestBodyReadStatus.TooLarge)]
    [InlineData(WebhookRequestBodyReadStatus.Malformed)]
    public async Task Standard_rejects_unreadable_body_with_bad_request(
        WebhookRequestBodyReadStatus status)
    {
        var reader = new Mock<IWebhookRequestBodyReader>();
        reader.Setup(x => x.ReadAsync(It.IsAny<HttpRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookRequestBodyReadResult(status, string.Empty));
        var intake = new Mock<IPaymentWebhookIntakeService>();
        var controller = Controller(reader.Object, intake.Object);

        var result = await controller.Standard();

        result.Should().BeOfType<BadRequestResult>();
        intake.Verify(x => x.AcceptStandardAsync(
            It.IsAny<StandardWebhookRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Standard_rejects_body_that_is_not_valid_json()
    {
        var reader = new Mock<IWebhookRequestBodyReader>();
        reader.Setup(x => x.ReadAsync(It.IsAny<HttpRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookRequestBodyReadResult.Success("not-json"));
        var intake = new Mock<IPaymentWebhookIntakeService>();
        var controller = Controller(reader.Object, intake.Object);

        var result = await controller.Standard();

        result.Should().BeOfType<BadRequestResult>();
        intake.Verify(x => x.AcceptStandardAsync(
            It.IsAny<StandardWebhookRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(WebhookIntakeOutcome.Accepted, StatusCodes.Status202Accepted)]
    [InlineData(WebhookIntakeOutcome.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(WebhookIntakeOutcome.Malformed, StatusCodes.Status400BadRequest)]
    [InlineData(WebhookIntakeOutcome.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(WebhookIntakeOutcome.StorageUnavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task Standard_maps_intake_outcome_to_status_code(
        WebhookIntakeOutcome outcome, int expectedStatus)
    {
        var reader = new Mock<IWebhookRequestBodyReader>();
        reader.Setup(x => x.ReadAsync(It.IsAny<HttpRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookRequestBodyReadResult.Success(EmptyNotifications));
        var intake = new Mock<IPaymentWebhookIntakeService>();
        intake.Setup(x => x.AcceptStandardAsync(
                It.IsAny<StandardWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        var controller = Controller(reader.Object, intake.Object);

        var result = await controller.Standard();

        StatusOf(result).Should().Be(expectedStatus);
    }

    [Fact]
    public async Task Standard_maps_unknown_outcome_to_internal_server_error()
    {
        var reader = new Mock<IWebhookRequestBodyReader>();
        reader.Setup(x => x.ReadAsync(It.IsAny<HttpRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookRequestBodyReadResult.Success(EmptyNotifications));
        var intake = new Mock<IPaymentWebhookIntakeService>();
        intake.Setup(x => x.AcceptStandardAsync(
                It.IsAny<StandardWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookIntakeOutcome)999);
        var controller = Controller(reader.Object, intake.Object);

        var result = await controller.Standard();

        StatusOf(result).Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task Tokens_rejects_unreadable_body_with_bad_request()
    {
        var reader = new Mock<IWebhookRequestBodyReader>();
        reader.Setup(x => x.ReadAsync(It.IsAny<HttpRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookRequestBodyReadResult.Malformed());
        var intake = new Mock<IPaymentWebhookIntakeService>();
        var controller = Controller(reader.Object, intake.Object);

        var result = await controller.Tokens();

        result.Should().BeOfType<BadRequestResult>();
        intake.Verify(x => x.AcceptTokenAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Tokens_rejects_unsupported_signature_protocol()
    {
        var reader = new Mock<IWebhookRequestBodyReader>();
        reader.Setup(x => x.ReadAsync(It.IsAny<HttpRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookRequestBodyReadResult.Success("{}"));
        var intake = new Mock<IPaymentWebhookIntakeService>();
        var controller = Controller(reader.Object, intake.Object);
        controller.HttpContext.Request.Headers["protocol"] = "HmacSHA1";

        var result = await controller.Tokens();

        result.Should().BeOfType<UnauthorizedResult>();
        intake.Verify(x => x.AcceptTokenAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Tokens_accepts_supported_protocol_and_forwards_signature()
    {
        var reader = new Mock<IWebhookRequestBodyReader>();
        reader.Setup(x => x.ReadAsync(It.IsAny<HttpRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookRequestBodyReadResult.Success("{\"token\":true}"));
        var intake = new Mock<IPaymentWebhookIntakeService>();
        intake.Setup(x => x.AcceptTokenAsync("{\"token\":true}", "sig-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookIntakeOutcome.Accepted);
        var controller = Controller(reader.Object, intake.Object);
        controller.HttpContext.Request.Headers["protocol"] = "hmacsha256";
        controller.HttpContext.Request.Headers["hmacsignature"] = "sig-123";

        var result = await controller.Tokens();

        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        intake.Verify(x => x.AcceptTokenAsync("{\"token\":true}", "sig-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tokens_accepts_missing_protocol_header()
    {
        var reader = new Mock<IWebhookRequestBodyReader>();
        reader.Setup(x => x.ReadAsync(It.IsAny<HttpRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookRequestBodyReadResult.Success("{}"));
        var intake = new Mock<IPaymentWebhookIntakeService>();
        intake.Setup(x => x.AcceptTokenAsync("{}", string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookIntakeOutcome.NotFound);
        var controller = Controller(reader.Object, intake.Object);

        var result = await controller.Tokens();

        StatusOf(result).Should().Be(StatusCodes.Status404NotFound);
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        AcceptedResult accepted => accepted.StatusCode ?? 0,
        StatusCodeResult status => status.StatusCode,
        ObjectResult obj => obj.StatusCode ?? 0,
        _ => 0
    };

    private static PaymentWebhooksController Controller(
        IWebhookRequestBodyReader reader,
        IPaymentWebhookIntakeService intake)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.Setup(x => x.CurrentValue).Returns(new PaymentOptions());

        var controller = new PaymentWebhooksController(
            intake,
            reader,
            Mock.Of<IHostApplicationLifetime>(),
            options.Object,
            NullLogger<PaymentWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.TraceIdentifier = "trace-1";
        return controller;
    }
}
