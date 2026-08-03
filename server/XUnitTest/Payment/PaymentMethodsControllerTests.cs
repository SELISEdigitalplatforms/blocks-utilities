using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentMethodsControllerTests
{
    [Fact]
    public async Task Get_returns_only_service_projection()
    {
        var query =
            new Mock<IStoredPaymentMethodQueryService>();
        query.Setup(service =>
                service.GetStoredPaymentMethodsAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new StoredPaymentMethodQueryResult(
                    true,
                    [
                        new StoredPaymentMethodResponse
                        {
                            PaymentMethodId = "method-1",
                            Type = "scheme",
                            LastFour = "1111",
                            Status = "ACTIVE"
                        }
                    ],
                    PaymentFailureKind.None,
                    string.Empty,
                    string.Empty));
        var controller = CreateController(
            query.Object,
            Mock.Of<IStoredPaymentMethodRemovalService>());

        var result =
            await controller.GetStoredPaymentMethods(
                CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Remove_returns_accepted_for_durable_pending_removal()
    {
        var removal =
            new Mock<IStoredPaymentMethodRemovalService>();
        removal.Setup(service =>
                service.RemoveStoredPaymentMethodAsync(
                    "method-1",
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new StoredPaymentMethodRemovalResult(
                    StoredPaymentMethodRemovalStatus.Pending,
                    PaymentFailureKind.None,
                    string.Empty,
                    string.Empty));
        var controller = CreateController(
            Mock.Of<IStoredPaymentMethodQueryService>(),
            removal.Object);

        var result =
            await controller.RemoveStoredPaymentMethod(
                "method-1",
                CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
        controller.Response.Headers.RetryAfter
            .ToString()
            .Should()
            .Be("30");
    }

    [Fact]
    public async Task Remove_returns_no_content_when_confirmed()
    {
        var removal =
            new Mock<IStoredPaymentMethodRemovalService>();
        removal.Setup(service =>
                service.RemoveStoredPaymentMethodAsync(
                    "method-1",
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new StoredPaymentMethodRemovalResult(
                    StoredPaymentMethodRemovalStatus.Removed,
                    PaymentFailureKind.None,
                    string.Empty,
                    string.Empty));
        var controller = CreateController(
            Mock.Of<IStoredPaymentMethodQueryService>(),
            removal.Object);

        var result =
            await controller.RemoveStoredPaymentMethod(
                "method-1",
                CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Get_maps_failure_and_applies_rate_limit_headers()
    {
        var query = new Mock<IStoredPaymentMethodQueryService>();
        query.Setup(service => service.GetStoredPaymentMethodsAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredPaymentMethodQueryResult(
                false,
                null,
                PaymentFailureKind.Unavailable,
                "provider_unavailable",
                "temporarily unavailable",
                new PaymentRateLimitResult
                {
                    Limit = 10,
                    Remaining = 3,
                    ResetAfterSeconds = 42,
                    RetryAfterSeconds = 7
                }));
        var controller = CreateController(
            query.Object, Mock.Of<IStoredPaymentMethodRemovalService>());

        var result = await controller.GetStoredPaymentMethods(CancellationToken.None);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        controller.Response.Headers["RateLimit-Limit"].ToString().Should().Be("10");
        controller.Response.Headers["RateLimit-Remaining"].ToString().Should().Be("3");
        controller.Response.Headers["RateLimit-Reset"].ToString().Should().Be("42");
        controller.Response.Headers.RetryAfter.ToString().Should().Be("7");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_rejects_blank_identifier(string paymentMethodId)
    {
        var controller = CreateController(
            Mock.Of<IStoredPaymentMethodQueryService>(),
            Mock.Of<IStoredPaymentMethodRemovalService>());

        var result = await controller.RemoveStoredPaymentMethod(
            paymentMethodId, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Remove_rejects_oversized_identifier()
    {
        var controller = CreateController(
            Mock.Of<IStoredPaymentMethodQueryService>(),
            Mock.Of<IStoredPaymentMethodRemovalService>());

        var result = await controller.RemoveStoredPaymentMethod(
            new string('a', 129), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(PaymentFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(PaymentFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(PaymentFailureKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(PaymentFailureKind.RateLimited, StatusCodes.Status429TooManyRequests)]
    [InlineData(PaymentFailureKind.Timeout, StatusCodes.Status504GatewayTimeout)]
    [InlineData(PaymentFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(PaymentFailureKind.Unexpected, StatusCodes.Status500InternalServerError)]
    public async Task Remove_maps_failure_kind_to_status_code(
        PaymentFailureKind failureKind, int expectedStatus)
    {
        var removal = new Mock<IStoredPaymentMethodRemovalService>();
        removal.Setup(service => service.RemoveStoredPaymentMethodAsync(
                "method-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredPaymentMethodRemovalResult(
                StoredPaymentMethodRemovalStatus.Failed,
                failureKind,
                "error_code",
                "error message"));
        var controller = CreateController(
            Mock.Of<IStoredPaymentMethodQueryService>(), removal.Object);

        var result = await controller.RemoveStoredPaymentMethod(
            "method-1", CancellationToken.None);

        if (expectedStatus == StatusCodes.Status400BadRequest)
        {
            result.Should().BeOfType<BadRequestObjectResult>();
        }
        else if (expectedStatus == StatusCodes.Status404NotFound)
        {
            result.Should().BeOfType<NotFoundObjectResult>();
        }
        else if (expectedStatus == StatusCodes.Status409Conflict)
        {
            result.Should().BeOfType<ConflictObjectResult>();
        }
        else
        {
            result.Should().BeOfType<ObjectResult>()
                .Which.StatusCode.Should().Be(expectedStatus);
        }
    }

    private static PaymentMethodsController CreateController(
        IStoredPaymentMethodQueryService query,
        IStoredPaymentMethodRemovalService removal) =>
        new(query, removal)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-1"
                }
            }
        };
}
