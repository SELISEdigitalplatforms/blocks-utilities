using Api.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// Which failures a client should retry, expressed as status codes.
/// </summary>
/// <remarks>
/// The distinction this pins down is between a request that cannot succeed until something
/// about the caller changes and one that may succeed on its own later. Both used to arrive as
/// <c>503</c>, which told integrations — and the infrastructure in front of them — to retry a
/// token that would never start working.
/// </remarks>
public sealed class SubscriptionApiResultsStatusTests
{
    private const string Correlation = "corr-1";

    [Theory]
    [InlineData("subscription_context_missing")]
    [InlineData("subscription_organization_missing")]
    public void A_caller_that_cannot_be_identified_is_told_so_with_401(string errorCode)
    {
        StatusFor(PaymentFailureKind.Unauthenticated, errorCode)
            .Should().Be(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// The kind this was split out of keeps its meaning: an organization IAM could not be
    /// reached to confirm is a genuine outage, and retrying it is the right thing to do.
    /// </summary>
    [Fact]
    public void A_genuine_outage_is_still_503()
    {
        StatusFor(PaymentFailureKind.Unavailable, "organization_verification_unavailable")
            .Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Theory]
    [InlineData(PaymentFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(PaymentFailureKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(PaymentFailureKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(PaymentFailureKind.RateLimited, StatusCodes.Status429TooManyRequests)]
    [InlineData(PaymentFailureKind.ProviderRejected, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(PaymentFailureKind.ProviderFailure, StatusCodes.Status502BadGateway)]
    [InlineData(PaymentFailureKind.Timeout, StatusCodes.Status504GatewayTimeout)]
    [InlineData(PaymentFailureKind.Unexpected, StatusCodes.Status500InternalServerError)]
    public void Every_other_failure_keeps_the_status_it_had(
        PaymentFailureKind kind,
        int expected)
    {
        // Inserting a kind into the enum must not shift what anything else maps to.
        StatusFor(kind, "some_error").Should().Be(expected);
    }

    [Fact]
    public void The_error_code_still_reaches_the_client()
    {
        var result = SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.Unauthenticated,
                "subscription_context_missing",
                "Authenticated tenant context is unavailable.",
                Correlation)
            .ToActionResult(Correlation);

        var body = result.Should().BeOfType<ObjectResult>().Subject.Value
            .Should().BeOfType<ApiResponse<string>>().Subject;

        body.Success.Should().BeFalse();
        body.Error!.Code.Should().Be("subscription_context_missing");
    }

    private static int? StatusFor(PaymentFailureKind kind, string errorCode) =>
        SubscriptionOperationResult<string>.Failure(kind, errorCode, "message", Correlation)
            .ToActionResult(Correlation)
            .Should().BeOfType<ObjectResult>().Subject.StatusCode;
}
