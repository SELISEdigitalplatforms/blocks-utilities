using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("payments/payment-methods")]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly IStoredPaymentMethodQueryService _queryService;
    private readonly IStoredPaymentMethodRemovalService
        _removalService;

    public PaymentMethodsController(
        IStoredPaymentMethodQueryService queryService,
        IStoredPaymentMethodRemovalService removalService)
    {
        _queryService = queryService;
        _removalService = removalService;
    }

    /// <param name="organizationId">
    /// Honoured only for the console, whose own organization is fixed; every other caller is
    /// answered under the organization their token carries. Cards are stamped with the
    /// organization that saved them, so without this the console can never see the cards saved
    /// by the payments it took for another organization.
    /// </param>
    [HttpGet]
    public async Task<IActionResult> GetStoredPaymentMethods(
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (organizationId?.Length > 200)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "invalid_organization_id",
                "The organization identifier is invalid.",
                correlationId);
        }

        var result =
            await _queryService.GetStoredPaymentMethodsAsync(
                organizationId,
                correlationId,
                cancellationToken);

        ApplyRateLimitHeaders(result.RateLimit);

        if (!result.IsSuccess)
        {
            return Failure(
                result.FailureKind,
                result.ErrorCode,
                result.ErrorMessage,
                correlationId);
        }

        return Ok(
            ApiResponse<
                IReadOnlyList<StoredPaymentMethodResponse>>.Ok(
                result.Methods!,
                correlationId));
    }

    [HttpDelete("{paymentMethodId}")]
    public async Task<IActionResult> RemoveStoredPaymentMethod(
        string paymentMethodId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (string.IsNullOrWhiteSpace(paymentMethodId) ||
            paymentMethodId.Length > 128)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "invalid_payment_method_id",
                "The payment method identifier is invalid.",
                correlationId);
        }

        var result =
            await _removalService.RemoveStoredPaymentMethodAsync(
                paymentMethodId,
                correlationId,
                cancellationToken);

        ApplyRateLimitHeaders(result.RateLimit);

        if (result.IsRemoved)
        {
            return NoContent();
        }

        if (result.IsPending)
        {
            Response.Headers.RetryAfter = "30";

            return Accepted(
                ApiResponse<
                    StoredPaymentMethodRemovalResponse>.Ok(
                    new StoredPaymentMethodRemovalResponse
                    {
                        PaymentMethodId = paymentMethodId
                    },
                    correlationId));
        }

        return Failure(
            result.FailureKind,
            result.ErrorCode,
            result.ErrorMessage,
            correlationId);
    }

    private IActionResult Failure(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId)
    {
        var response = ApiResponse<object>.Fail(
            errorCode,
            errorMessage,
            correlationId);

        return failureKind switch
        {
            PaymentFailureKind.Validation =>
                BadRequest(response),
            PaymentFailureKind.NotFound =>
                NotFound(response),
            PaymentFailureKind.Conflict =>
                Conflict(response),
            PaymentFailureKind.RateLimited =>
                StatusCode(
                    StatusCodes.Status429TooManyRequests,
                    response),
            PaymentFailureKind.Timeout =>
                StatusCode(
                    StatusCodes.Status504GatewayTimeout,
                    response),
            PaymentFailureKind.Unavailable =>
                StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    response),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                response)
        };
    }

    private void ApplyRateLimitHeaders(
        PaymentRateLimitResult? rateLimit)
    {
        if (rateLimit == null)
        {
            return;
        }

        Response.Headers["RateLimit-Limit"] =
            rateLimit.Limit.ToString();
        Response.Headers["RateLimit-Remaining"] =
            rateLimit.Remaining.ToString();
        Response.Headers["RateLimit-Reset"] =
            rateLimit.ResetAfterSeconds.ToString();

        if (rateLimit.RetryAfterSeconds > 0)
        {
            Response.Headers.RetryAfter =
                rateLimit.RetryAfterSeconds.ToString();
        }
    }
}
