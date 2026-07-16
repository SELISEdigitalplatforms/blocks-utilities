using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace Api.Controllers;

[ApiController]
[Route("payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [Authorize]
    [HttpPost("create")]
    public async Task<IActionResult> CreatePayment(
        [FromBody] MakePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _paymentService.MakePaymentAsync(request, idempotencyKey ?? string.Empty, correlationId, cancellationToken);
        ApplyRateLimitHeaders(result);
        if (!result.IsSuccess) return Failure(result);

        var response = ApiResponse<PaymentResponse>.Ok(result.Payment!, correlationId, result.IsReplay);
        return result.IsReplay
            ? Ok(response)
            : CreatedAtAction(nameof(GetPayment), new { paymentDetailId = result.Payment!.PaymentDetailId }, response);
    }

    [HttpGet("{paymentDetailId}")]
    public async Task<IActionResult> GetPayment(string paymentDetailId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _paymentService.GetPaymentAsync(paymentDetailId, correlationId, cancellationToken);
        if (!result.IsSuccess) return Failure(result);
        return Ok(ApiResponse<PaymentResponse>.Ok(result.Payment!, correlationId));
    }

    private IActionResult Failure(PaymentOperationResult result)
    {
        var response = ApiResponse<PaymentResponse>.Fail(
            result.ErrorCode,
            result.ErrorMessage,
            result.CorrelationId,
            result.ValidationErrors);
        return result.FailureKind switch
        {
            PaymentFailureKind.Validation => BadRequest(response),
            PaymentFailureKind.NotFound => NotFound(response),
            PaymentFailureKind.Conflict => Conflict(response),
            PaymentFailureKind.RateLimited => StatusCode(StatusCodes.Status429TooManyRequests, response),
            PaymentFailureKind.ProviderRejected => UnprocessableEntity(response),
            PaymentFailureKind.ProviderFailure => StatusCode(StatusCodes.Status502BadGateway, response),
            PaymentFailureKind.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            PaymentFailureKind.Timeout => StatusCode(StatusCodes.Status504GatewayTimeout, response),
            _ => StatusCode(StatusCodes.Status500InternalServerError, response)
        };
    }

    private void ApplyRateLimitHeaders(PaymentOperationResult result)
    {
        if (result.RateLimit.HasValue) Response.Headers["RateLimit-Limit"] = result.RateLimit.Value.ToString();
        if (result.RateLimitRemaining.HasValue) Response.Headers["RateLimit-Remaining"] = result.RateLimitRemaining.Value.ToString();
        if (result.RateLimitResetSeconds.HasValue) Response.Headers["RateLimit-Reset"] = result.RateLimitResetSeconds.Value.ToString();
        if (result.RetryAfterSeconds.HasValue) Response.Headers.RetryAfter = result.RetryAfterSeconds.Value.ToString();
    }
}
