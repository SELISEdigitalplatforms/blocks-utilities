using System.Reflection;
using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>The metered overage preview endpoint's HTTP-facing behaviour.</summary>
public sealed class SubscriptionUsageControllerTests
{
    [Fact]
    public void The_controller_requires_authentication()
    {
        typeof(SubscriptionUsageController)
            .GetCustomAttribute<AuthorizeAttribute>()
            .Should().NotBeNull("every subscription-usage endpoint, including the preview, must " +
                "be reached only by an authenticated caller");
    }

    [Fact]
    public async Task A_successful_preview_returns_the_envelope_with_the_correlation_id()
    {
        var overage = new Mock<ISubscriptionUsageOveragePreviewService>();
        overage
            .Setup(service => service.PreviewAsync(
                It.IsAny<PreviewUsageOverageRequest>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<SubscriptionUsageOveragePreviewResponse>.Success(
                new SubscriptionUsageOveragePreviewResponse { MeterKey = "screening" }, "trace-1"));
        var controller = Controller(overage.Object);

        var result = await controller.PreviewOverage(
            new PreviewUsageOverageRequest { MeterKey = "screening", AdditionalQuantity = 100 },
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should()
            .BeOfType<ApiResponse<SubscriptionUsageOveragePreviewResponse>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data!.MeterKey.Should().Be("screening");
        envelope.Meta.CorrelationId.Should().Be("trace-1");
    }

    [Fact]
    public async Task A_validation_failure_returns_400_with_the_named_error_code()
    {
        var overage = new Mock<ISubscriptionUsageOveragePreviewService>();
        overage
            .Setup(service => service.PreviewAsync(
                It.IsAny<PreviewUsageOverageRequest>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<SubscriptionUsageOveragePreviewResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_usage_preview_invalid",
                "The overage preview request is invalid.",
                "trace-1"));
        var controller = Controller(overage.Object);

        var result = await controller.PreviewOverage(
            new PreviewUsageOverageRequest(), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var envelope = objectResult.Value.Should()
            .BeOfType<ApiResponse<SubscriptionUsageOveragePreviewResponse>>().Subject;
        envelope.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("subscription_usage_preview_invalid");
    }

    [Theory]
    [InlineData(PaymentFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(PaymentFailureKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(PaymentFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task Named_failures_map_to_their_documented_status_code(
        PaymentFailureKind kind, int expectedStatus)
    {
        var overage = new Mock<ISubscriptionUsageOveragePreviewService>();
        overage
            .Setup(service => service.PreviewAsync(
                It.IsAny<PreviewUsageOverageRequest>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<SubscriptionUsageOveragePreviewResponse>.Failure(
                kind, "some_error_code", "failed", "trace-1"));
        var controller = Controller(overage.Object);

        var result = await controller.PreviewOverage(
            new PreviewUsageOverageRequest { MeterKey = "screening", AdditionalQuantity = 1 },
            CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task The_organization_id_on_the_request_body_is_forwarded_to_the_service()
    {
        var overage = new Mock<ISubscriptionUsageOveragePreviewService>();
        overage
            .Setup(service => service.PreviewAsync(
                It.IsAny<PreviewUsageOverageRequest>(), "trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<SubscriptionUsageOveragePreviewResponse>.Success(
                new SubscriptionUsageOveragePreviewResponse(), "trace-1"));
        var controller = Controller(overage.Object);

        await controller.PreviewOverage(
            new PreviewUsageOverageRequest
            {
                MeterKey = "screening",
                AdditionalQuantity = 1,
                OrganizationId = "org-9"
            },
            CancellationToken.None);

        overage.Verify(
            service => service.PreviewAsync(
                It.Is<PreviewUsageOverageRequest>(r => r.OrganizationId == "org-9"),
                "trace-1",
                It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches the service");
    }

    private static SubscriptionUsageController Controller(
        ISubscriptionUsageOveragePreviewService overagePreview)
    {
        var controller = new SubscriptionUsageController(
            Mock.Of<IUsageRecordingService>(), overagePreview)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.TraceIdentifier = "trace-1";
        return controller;
    }
}
