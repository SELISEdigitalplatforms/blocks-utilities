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
