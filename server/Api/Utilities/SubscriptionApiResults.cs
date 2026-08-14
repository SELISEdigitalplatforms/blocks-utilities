using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Api.Utilities;

/// <summary>
/// Maps a subscription result onto an HTTP response.
/// </summary>
/// <remarks>
/// One mapping for every subscription controller. Repeated per controller, the switches drift:
/// one starts returning 404 where another returns 409 for the same failure, and clients end up
/// coding against whichever endpoint they met first.
/// </remarks>
public static class SubscriptionApiResults
{
    public static IActionResult ToActionResult<TValue>(
        this SubscriptionOperationResult<TValue> result,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return new OkObjectResult(
                ApiResponse<TValue>.Ok(result.Value!, correlationId));
        }

        var response = ApiResponse<TValue>.Fail(
            result.ErrorCode ?? "subscription_failed",
            result.ErrorMessage ?? "The subscription operation failed.",
            correlationId,
            result.ValidationErrors?.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal));

        return new ObjectResult(response) { StatusCode = StatusCodeFor(result.FailureKind) };
    }

    private static int StatusCodeFor(PaymentFailureKind kind) => kind switch
    {
        PaymentFailureKind.Validation => StatusCodes.Status400BadRequest,
        PaymentFailureKind.NotFound => StatusCodes.Status404NotFound,
        PaymentFailureKind.Conflict => StatusCodes.Status409Conflict,
        PaymentFailureKind.RateLimited => StatusCodes.Status429TooManyRequests,
        PaymentFailureKind.ProviderRejected => StatusCodes.Status422UnprocessableEntity,
        PaymentFailureKind.ProviderFailure => StatusCodes.Status502BadGateway,
        PaymentFailureKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        PaymentFailureKind.Timeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status500InternalServerError
    };
}
