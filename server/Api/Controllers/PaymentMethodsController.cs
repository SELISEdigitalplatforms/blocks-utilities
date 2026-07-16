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
    private readonly IStoredPaymentMethodService _service;
    public PaymentMethodsController(IStoredPaymentMethodService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _service.ListAsync(correlationId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<IReadOnlyList<StoredPaymentMethodResponse>>.Ok(result.Methods!, correlationId))
            : Failure(result, correlationId);
    }

    [HttpDelete("{paymentMethodId}")]
    public async Task<IActionResult> Delete(string paymentMethodId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId) || paymentMethodId.Length > 128) return BadRequest();
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _service.DeleteAsync(paymentMethodId, correlationId, cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result, correlationId);
    }

    private IActionResult Failure(StoredPaymentMethodOperationResult result, string correlationId)
    {
        var response = ApiResponse<object>.Fail(result.ErrorCode, result.ErrorMessage, correlationId);
        return result.FailureKind switch
        {
            PaymentFailureKind.NotFound => NotFound(response),
            PaymentFailureKind.Timeout => StatusCode(StatusCodes.Status504GatewayTimeout, response),
            PaymentFailureKind.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => BadRequest(response)
        };
    }
}
