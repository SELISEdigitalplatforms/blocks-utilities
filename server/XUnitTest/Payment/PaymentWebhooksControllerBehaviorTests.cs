using Api.Controllers;
using Api.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Providers;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentWebhooksControllerBehaviorTests
{
    [Fact]
    public async Task Unreadable_body_is_rejected_without_reaching_intake()
    {
        var intake = new Mock<IPaymentWebhookIntakeService>();
        var controller = Controller(
            Reader(WebhookRequestBodyReadResult.Malformed()),
            intake.Object);

        (await controller.Adyen()).Should().BeOfType<BadRequestResult>();

        intake.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Oversized_body_is_rejected_without_reaching_intake()
    {
        var intake = new Mock<IPaymentWebhookIntakeService>();
        var controller = Controller(
            Reader(WebhookRequestBodyReadResult.TooLarge()),
            intake.Object);

        (await controller.Adyen()).Should().BeOfType<BadRequestResult>();

        intake.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unregistered_provider_is_not_forwarded_to_intake()
    {
        var intake = new Mock<IPaymentWebhookIntakeService>();
        var controller = Controller(
            Reader(WebhookRequestBodyReadResult.Success("{}")),
            intake.Object);

        (await controller.Provider("STRIPE")).Should().BeOfType<NotFoundResult>();

        intake.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Body_and_headers_are_forwarded_verbatim()
    {
        var intake = new Mock<IPaymentWebhookIntakeService>();
        intake.Setup(x => x.AcceptAsync(
                PaymentConstants.AdyenOnlineProvider,
                "{\"token\":true}",
                It.Is<IReadOnlyDictionary<string, string>>(headers =>
                    headers["hmacsignature"] == "sig-123"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookIntakeOutcome.Accepted);
        var controller = Controller(
            Reader(WebhookRequestBodyReadResult.Success("{\"token\":true}")),
            intake.Object);
        controller.HttpContext.Request.Headers["hmacsignature"] = "sig-123";

        var result = await controller.Adyen();

        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        intake.VerifyAll();
    }

    [Theory]
    [InlineData(WebhookIntakeOutcome.Accepted, StatusCodes.Status202Accepted)]
    [InlineData(WebhookIntakeOutcome.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(WebhookIntakeOutcome.Malformed, StatusCodes.Status400BadRequest)]
    [InlineData(WebhookIntakeOutcome.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(WebhookIntakeOutcome.StorageUnavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task Intake_outcomes_map_to_status_codes(
        WebhookIntakeOutcome outcome,
        int expected)
    {
        var intake = new Mock<IPaymentWebhookIntakeService>();
        intake.Setup(x => x.AcceptAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        var controller = Controller(
            Reader(WebhookRequestBodyReadResult.Success("{}")),
            intake.Object);

        StatusOf(await controller.Adyen()).Should().Be(expected);
    }

    private static Mock<IWebhookRequestBodyReader> ReaderMock(WebhookRequestBodyReadResult result)
    {
        var reader = new Mock<IWebhookRequestBodyReader>();
        reader.Setup(x => x.ReadAsync(
                It.IsAny<HttpRequest>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        return reader;
    }

    private static IWebhookRequestBodyReader Reader(WebhookRequestBodyReadResult result) =>
        ReaderMock(result).Object;

    private static PaymentWebhooksController Controller(
        IWebhookRequestBodyReader reader,
        IPaymentWebhookIntakeService intake)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(x => x.CurrentValue).Returns(new PaymentOptions());

        return new PaymentWebhooksController(
            intake,
            new PaymentProviderCatalog(),
            reader,
            Mock.Of<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(),
            options.Object,
            NullLogger<PaymentWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        StatusCodeResult status => status.StatusCode,
        ObjectResult obj => obj.StatusCode ?? 0,
        _ => 0
    };
}
