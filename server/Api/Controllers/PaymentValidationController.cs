using Api.Utilities;
using BlocksTemplate.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;

namespace Api.Controllers;

[ApiController]
[AllowAnonymous]
[SkipGlobalApiRoutePrefix]
[Route("payments/validate")]
public sealed class PaymentValidationController : ControllerBase
{
    private readonly ICheckoutCallbackService _callbackService;

    public PaymentValidationController(ICheckoutCallbackService callbackService)
    {
        _callbackService = callbackService;
    }

    [HttpGet]
    public async Task<IActionResult> ValidatePayment(
        [FromQuery] string? state,
        [FromQuery] string? sessionId,
        [FromQuery] string? sessionResult,
        CancellationToken cancellationToken)
    {
        var request = new CheckoutCallbackRequest(
            state,
            sessionId,
            sessionResult);

        var clientAddress =
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var result = await _callbackService.ProcessAsync(
            request,
            clientAddress,
            cancellationToken);

        return CheckoutCallbackHttpResultMapper.Map(this, result);
    }
}
