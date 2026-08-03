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

/// <summary>
/// The controller's only job is to turn a failure kind into the right status
/// code and to keep the correlation id on every envelope, so each kind gets its
/// own case.
/// </summary>
public sealed class PaymentProvidersControllerTests
{
    private const string TraceId = "trace-1";

    private readonly Mock<IPaymentProviderQueryService> _queryService = new();
    private readonly Mock<IPaymentProviderConfigurationService>
        _configurationService = new();
    private readonly Mock<IPaymentProviderCredentialRotationService>
        _rotationService = new();
    private readonly Mock<IPaymentEncryptionAdminService> _encryptionService = new();

    private PaymentProvidersController Controller()
    {
        var controller = new PaymentProvidersController(
            _queryService.Object,
            _configurationService.Object,
            _rotationService.Object,
            _encryptionService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.TraceIdentifier = TraceId;

        return controller;
    }

    private static PaymentProviderResponse ProviderResponse() => new()
    {
        PaymentProviderId = "provider-1",
        ProviderName = "stripe",
        Version = 4
    };

    private static UpdatePaymentProviderRequest UpdateRequest() => new()
    {
        Version = 3,
        FrontendResultUrl = "https://merchant.example/result"
    };

    private static RotatePaymentProviderCredentialsRequest RotateRequest() =>
        new()
        {
            Version = 3,
            ApiKey = "sk_test_new"
        };

    private void UpdateReturns(PaymentProviderMutationResult result) =>
        _configurationService.Setup(x => x.UpdateAsync(
                It.IsAny<string>(),
                It.IsAny<UpdatePaymentProviderRequest>(),
                TraceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void RotateReturns(PaymentProviderMutationResult result) =>
        _rotationService.Setup(x => x.RotateAsync(
                It.IsAny<string>(),
                It.IsAny<RotatePaymentProviderCredentialsRequest>(),
                TraceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task Listing_providers_returns_the_envelope_with_the_trace_id()
    {
        _queryService.Setup(x => x.GetProvidersAsync(
                TraceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentProviderListResult.Success(
                [ProviderResponse()],
                TraceId));

        var result = await Controller().GetPaymentProviders(
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should()
            .BeOfType<ApiResponse<IReadOnlyList<PaymentProviderResponse>>>()
            .Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().ContainSingle();
        envelope.Meta.CorrelationId.Should().Be(TraceId);
    }

    [Fact]
    public async Task An_unavailable_provider_store_lists_as_503()
    {
        _queryService.Setup(x => x.GetProvidersAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentProviderListResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_provider_store_unavailable",
                "Payment providers are temporarily unavailable.",
                TraceId));

        var result = await Controller().GetPaymentProviders(
            CancellationToken.None);

        var status = result.Should().BeAssignableTo<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        status.Value.Should()
            .BeOfType<ApiResponse<IReadOnlyList<PaymentProviderResponse>>>()
            .Which.Error!.Code.Should().Be("payment_provider_store_unavailable");
    }

    [Fact]
    public async Task An_unclassified_listing_failure_is_a_500()
    {
        _queryService.Setup(x => x.GetProvidersAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentProviderListResult.Failure(
                PaymentFailureKind.Unexpected,
                "payment_provider_unexpected",
                "Something went wrong.",
                TraceId));

        var result = await Controller().GetPaymentProviders(
            CancellationToken.None);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode
            .Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task Updating_a_provider_returns_the_updated_representation()
    {
        UpdateReturns(PaymentProviderMutationResult.Success(
            ProviderResponse(),
            TraceId));

        var result = await Controller().UpdatePaymentProvider(
            "provider-1",
            UpdateRequest(),
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<PaymentProviderResponse>>()
            .Which.Data!.Version.Should().Be(4);
    }

    [Fact]
    public async Task The_provider_id_and_body_are_passed_through_unchanged()
    {
        UpdateReturns(PaymentProviderMutationResult.Success(
            ProviderResponse(),
            TraceId));
        var request = UpdateRequest();

        await Controller().UpdatePaymentProvider(
            "provider-9",
            request,
            CancellationToken.None);

        _configurationService.Verify(
            x => x.UpdateAsync(
                "provider-9",
                request,
                TraceId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(PaymentFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(PaymentFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(PaymentFailureKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(PaymentFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(PaymentFailureKind.Unexpected, StatusCodes.Status500InternalServerError)]
    [InlineData(PaymentFailureKind.Timeout, StatusCodes.Status500InternalServerError)]
    public async Task Every_update_failure_kind_maps_to_its_status_code(
        PaymentFailureKind failureKind,
        int expectedStatusCode)
    {
        UpdateReturns(PaymentProviderMutationResult.Failure(
            failureKind,
            "payment_provider_error",
            "It did not work.",
            TraceId));

        var result = await Controller().UpdatePaymentProvider(
            "provider-1",
            UpdateRequest(),
            CancellationToken.None);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(expectedStatusCode);
    }

    [Fact]
    public async Task Field_level_validation_errors_survive_onto_the_envelope()
    {
        UpdateReturns(PaymentProviderMutationResult.Failure(
            PaymentFailureKind.Validation,
            "payment_provider_request_invalid",
            "A result url is required.",
            TraceId,
            new Dictionary<string, string[]>
            {
                ["frontendResultUrl"] = ["A result url is required."]
            }));

        var result = await Controller().UpdatePaymentProvider(
            "provider-1",
            UpdateRequest(),
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var envelope = badRequest.Value.Should()
            .BeOfType<ApiResponse<PaymentProviderResponse>>()
            .Subject;
        envelope.Error!.Fields.Should().ContainKey("frontendResultUrl");
        envelope.Error.TraceId.Should().Be(TraceId);
    }

    [Fact]
    public async Task Rotating_credentials_returns_the_updated_representation()
    {
        RotateReturns(PaymentProviderMutationResult.Success(
            ProviderResponse(),
            TraceId));

        var result = await Controller().RotatePaymentProviderCredentials(
            "provider-1",
            RotateRequest(),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<ApiResponse<PaymentProviderResponse>>()
            .Which.Success.Should().BeTrue();
    }

    [Fact]
    public async Task A_rotation_response_never_carries_a_credential_back_out()
    {
        RotateReturns(PaymentProviderMutationResult.Success(
            ProviderResponse(),
            TraceId));

        var result = await Controller().RotatePaymentProviderCredentials(
            "provider-1",
            RotateRequest(),
            CancellationToken.None);

        System.Text.Json.JsonSerializer
            .Serialize(((OkObjectResult)result).Value)
            .Should().NotContain("sk_test_new");
    }

    [Theory]
    [InlineData(PaymentFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(PaymentFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(PaymentFailureKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(PaymentFailureKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(PaymentFailureKind.ProviderFailure, StatusCodes.Status500InternalServerError)]
    public async Task Every_rotation_failure_kind_maps_to_its_status_code(
        PaymentFailureKind failureKind,
        int expectedStatusCode)
    {
        RotateReturns(PaymentProviderMutationResult.Failure(
            failureKind,
            "payment_provider_error",
            "It did not work.",
            TraceId));

        var result = await Controller().RotatePaymentProviderCredentials(
            "provider-1",
            RotateRequest(),
            CancellationToken.None);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(expectedStatusCode);
    }

    [Fact]
    public async Task The_rotation_id_and_body_are_passed_through_unchanged()
    {
        RotateReturns(PaymentProviderMutationResult.Success(
            ProviderResponse(),
            TraceId));
        var request = RotateRequest();

        await Controller().RotatePaymentProviderCredentials(
            "provider-9",
            request,
            CancellationToken.None);

        _rotationService.Verify(
            x => x.RotateAsync(
                "provider-9",
                request,
                TraceId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
