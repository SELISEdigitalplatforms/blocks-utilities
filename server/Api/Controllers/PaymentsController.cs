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
    private readonly IRecurringPaymentService
        _recurringPaymentService;
    private readonly IPaymentRefundService _refundService;
    private readonly IPaymentCaptureService _captureService;
    private readonly IPaymentQueryService _paymentQueryService;
    private readonly IPaymentProviderRegistrationService _providerRegistrationService;

    public PaymentsController(
        IPaymentService paymentService,
        IRecurringPaymentService recurringPaymentService,
        IPaymentRefundService refundService,
        IPaymentCaptureService captureService,
        IPaymentQueryService paymentQueryService,
        IPaymentProviderRegistrationService providerRegistrationService)
    {
        _paymentService = paymentService;
        _recurringPaymentService = recurringPaymentService;
        _refundService = refundService;
        _captureService = captureService;
        _paymentQueryService = paymentQueryService;
        _providerRegistrationService = providerRegistrationService;
    }

    [Authorize]
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentListResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentListResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentListResponse>),
        StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentListResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetPayments(
        [FromQuery] GetPaymentsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _paymentQueryService.GetPaymentsAsync(
            request,
            correlationId,
            cancellationToken);

        ApplyRateLimitHeaders(result.RateLimit);

        return result.IsSuccess
            ? Ok(ApiResponse<PaymentListResponse>.Ok(
                result.Response!,
                correlationId))
            : PaymentQueryFailure(result);
    }

    /// <summary>
    /// Registers a payment provider for the calling tenant. Credentials are encrypted before
    /// storage and are never returned.
    /// </summary>
    [Authorize]
    [HttpPost("providers")]
    [ProducesResponseType(typeof(ApiResponse<PaymentResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<PaymentResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PaymentResponse>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterProvider(
        [FromBody] RegisterPaymentProviderRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _providerRegistrationService.RegisterAsync(
            request,
            correlationId,
            cancellationToken);

        return result.IsSuccess
            ? Created(
                string.Empty,
                ApiResponse<PaymentResponse>.Ok(result.Payment!, correlationId))
            : Failure(result);
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

    [Authorize]
    [HttpPost("recurring-payments")]
    public async Task<IActionResult> CreateRecurringPayment(
        [FromBody] CreateRecurringPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")]
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result =
            await _recurringPaymentService
                .CreateRecurringPaymentAsync(
                    request,
                    idempotencyKey ?? string.Empty,
                    correlationId,
                    cancellationToken);

        ApplyRateLimitHeaders(result);

        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        var response = ApiResponse<PaymentResponse>.Ok(
            result.Payment!,
            correlationId,
            result.IsReplay);

        return result.IsReplay
            ? Ok(response)
            : CreatedAtAction(
                nameof(GetPayment),
                new
                {
                    paymentDetailId =
                        result.Payment!.PaymentDetailId
                },
                response);
    }

    [HttpGet("{paymentDetailId}")]
    public async Task<IActionResult> GetPayment(string paymentDetailId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _paymentService.GetPaymentAsync(paymentDetailId, correlationId, cancellationToken);
        if (!result.IsSuccess) return Failure(result);
        return Ok(ApiResponse<PaymentResponse>.Ok(result.Payment!, correlationId));
    }

    [Authorize]
    [HttpPost("{paymentDetailId}/refunds")]
    public async Task<IActionResult> CreatePaymentRefund(
        string paymentDetailId,
        [FromBody] CreatePaymentRefundRequest request,
        [FromHeader(Name = "Idempotency-Key")]
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result =
            await _refundService.CreatePaymentRefundAsync(
                paymentDetailId,
                request,
                idempotencyKey ?? string.Empty,
                correlationId,
                cancellationToken);

        ApplyRateLimitHeaders(result.RateLimit);

        if (!result.IsSuccess)
        {
            return RefundFailure(result);
        }

        var response = ApiResponse<PaymentRefundResponse>.Ok(
            result.Refund!,
            correlationId,
            result.IsReplay);

        return result.IsReplay
            ? Ok(response)
            : CreatedAtAction(
                nameof(GetPaymentRefund),
                new
                {
                    paymentDetailId,
                    refundId = result.Refund!.RefundId
                },
                response);
    }

    [Authorize]
    [HttpPost("{paymentDetailId}/captures")]
    public async Task<IActionResult> CreatePaymentCapture(
        string paymentDetailId,
        [FromBody] CreatePaymentCaptureRequest request,
        [FromHeader(Name = "Idempotency-Key")]
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _captureService.CreatePaymentCaptureAsync(
            paymentDetailId,
            request,
            idempotencyKey ?? string.Empty,
            correlationId,
            cancellationToken);

        ApplyRateLimitHeaders(result.RateLimit);

        if (!result.IsSuccess)
        {
            return CaptureFailure(result);
        }

        var response = ApiResponse<PaymentCaptureResponse>.Ok(
            result.Capture!,
            correlationId,
            result.IsReplay);

        return result.IsReplay
            ? Ok(response)
            : CreatedAtAction(
                nameof(GetPaymentCapture),
                new
                {
                    paymentDetailId,
                    captureId = result.Capture!.CaptureId
                },
                response);
    }

    [Authorize]
    [HttpGet("{paymentDetailId}/captures/{captureId}")]
    public async Task<IActionResult> GetPaymentCapture(
        string paymentDetailId,
        string captureId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _captureService.GetPaymentCaptureAsync(
            paymentDetailId,
            captureId,
            correlationId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<PaymentCaptureResponse>.Ok(
                result.Capture!,
                correlationId))
            : CaptureFailure(result);
    }

    [Authorize]
    [HttpGet("{paymentDetailId}/refunds/{refundId}")]
    public async Task<IActionResult> GetPaymentRefund(
        string paymentDetailId,
        string refundId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result =
            await _refundService.GetPaymentRefundAsync(
                paymentDetailId,
                refundId,
                correlationId,
                cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<PaymentRefundResponse>.Ok(
                result.Refund!,
                correlationId))
            : RefundFailure(result);
    }

    [Authorize]
    [HttpGet("{paymentDetailId}/refunds")]
    public async Task<IActionResult> GetPaymentRefunds(
        string paymentDetailId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var (refunds, failure) =
            await _refundService.GetPaymentRefundsAsync(
                paymentDetailId,
                correlationId,
                cancellationToken);

        return failure == null
            ? Ok(ApiResponse<
                IReadOnlyList<PaymentRefundResponse>>.Ok(
                    refunds!,
                    correlationId))
            : RefundFailure(failure);
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

    private IActionResult RefundFailure(
        PaymentRefundOperationResult result)
    {
        var response = ApiResponse<PaymentRefundResponse>.Fail(
            result.ErrorCode,
            result.ErrorMessage,
            result.CorrelationId,
            result.ValidationErrors);

        return result.FailureKind switch
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
            PaymentFailureKind.ProviderRejected =>
                UnprocessableEntity(response),
            PaymentFailureKind.ProviderFailure =>
                StatusCode(
                    StatusCodes.Status502BadGateway,
                    response),
            PaymentFailureKind.Unavailable =>
                StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    response),
            PaymentFailureKind.Timeout =>
                StatusCode(
                    StatusCodes.Status504GatewayTimeout,
                    response),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                response)
        };
    }

    private IActionResult CaptureFailure(
        PaymentCaptureOperationResult result)
    {
        var response = ApiResponse<PaymentCaptureResponse>.Fail(
            result.ErrorCode,
            result.ErrorMessage,
            result.CorrelationId,
            result.ValidationErrors);

        return result.FailureKind switch
        {
            PaymentFailureKind.Validation => BadRequest(response),
            PaymentFailureKind.NotFound => NotFound(response),
            PaymentFailureKind.Conflict => Conflict(response),
            PaymentFailureKind.RateLimited => StatusCode(
                StatusCodes.Status429TooManyRequests,
                response),
            PaymentFailureKind.ProviderRejected =>
                UnprocessableEntity(response),
            PaymentFailureKind.ProviderFailure => StatusCode(
                StatusCodes.Status502BadGateway,
                response),
            PaymentFailureKind.Unavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                response),
            PaymentFailureKind.Timeout => StatusCode(
                StatusCodes.Status504GatewayTimeout,
                response),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                response)
        };
    }

    private IActionResult PaymentQueryFailure(
        PaymentQueryOperationResult result)
    {
        var response = ApiResponse<PaymentListResponse>.Fail(
            result.ErrorCode,
            result.ErrorMessage,
            result.CorrelationId,
            result.ValidationErrors);

        return result.FailureKind switch
        {
            PaymentFailureKind.Validation => BadRequest(response),
            PaymentFailureKind.RateLimited => StatusCode(
                StatusCodes.Status429TooManyRequests,
                response),
            PaymentFailureKind.Unavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                response),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                response)
        };
    }
}
